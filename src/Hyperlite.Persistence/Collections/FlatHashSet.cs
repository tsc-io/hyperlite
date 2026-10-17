namespace Hyperlite.Persistence.Collections
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;

    /// <summary>
    ///     A flat, open addressed hash set in the manner of Abseil's Swiss table, with the
    ///     comparer as a struct type argument.
    /// </summary>
    /// <typeparam name="T">
    ///     The element type.
    /// </typeparam>
    /// <typeparam name="TComparer">
    ///     The comparer type, see <see cref="FlatDictionary{TKey, TValue, TComparer}" />.
    /// </typeparam>
    /// <remarks>
    ///     Meant as a drop-in for <see cref="HashSet{T}" />, with the same caveat as the
    ///     dictionary: <b>the enumeration order is the slot order, not the insertion
    ///     order.</b> The probing is the dictionary's; see <see cref="FlatControl" /> for
    ///     the layout. Unlike <see cref="HashSet{T}" /> a null element is refused, as the
    ///     dictionary refuses a null key - every comparer this is used with would throw on
    ///     one or treat it as nothing.
    /// </remarks>
    [DebuggerDisplay("Count = {Count}")]
    internal class FlatHashSet<T, TComparer> : ISet<T>, IReadOnlyCollection<T>
#if NET5_0_OR_GREATER
        , IReadOnlySet<T>
#endif
        where TComparer : struct, IEqualityComparer<T>
    {
        private static readonly Slot[] NoSlots = new Slot[0];

        /// <summary>
        ///     The comparer. Not readonly, see the dictionary.
        /// </summary>
        private TComparer comparer;

        private ulong[] control;

        /// <summary>
        ///     The elements, each wrapped in a struct. A store into a <c>T[]</c> of a
        ///     reference type goes through the array covariance check, a helper call per
        ///     store in shared generic code; a store into a field of a struct element does
        ///     not. That is most of what growing a set from empty used to cost.
        /// </summary>
        private Slot[] slots;

        private int groupMask;

        private int count;

        private int growthLeft;

        private int version;

        public FlatHashSet()
            : this(0, default(TComparer))
        {
        }

        /// <param name="capacity">
        ///     The number of elements the set can hold without growing.
        /// </param>
        public FlatHashSet(int capacity)
            : this(capacity, default(TComparer))
        {
        }

        /// <param name="comparer">
        ///     The comparer, for a comparer struct that carries state.
        /// </param>
        public FlatHashSet(TComparer comparer)
            : this(0, comparer)
        {
        }

        /// <param name="capacity">
        ///     The number of elements the set can hold without growing.
        /// </param>
        /// <param name="comparer">
        ///     The comparer, for a comparer struct that carries state.
        /// </param>
        public FlatHashSet(int capacity, TComparer comparer)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            this.comparer = comparer;
            if (capacity == 0)
            {
                this.control = FlatControl.EmptyGroup;
                this.slots = NoSlots;
            }
            else
            {
                this.Allocate(FlatControl.SlotsFor(capacity));
            }
        }

        /// <param name="collection">
        ///     The elements to add; duplicates are dropped.
        /// </param>
        public FlatHashSet(IEnumerable<T> collection)
            : this(collection is ICollection<T> c ? c.Count : 0, default(TComparer))
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            this.UnionWith(collection);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FlatHashSet{T, TComparer}" /> class as a
        ///     copy of another, slot for slot - see the dictionary's copy constructor.
        /// </summary>
        internal FlatHashSet(FlatHashSet<T, TComparer> source)
        {
            this.comparer = source.comparer;
            this.groupMask = source.groupMask;
            this.count = source.count;
            this.growthLeft = source.growthLeft;
            if (source.slots.Length == 0)
            {
                this.control = FlatControl.EmptyGroup;
                this.slots = NoSlots;
            }
            else
            {
                this.control = (ulong[])source.control.Clone();
                this.slots = (Slot[])source.slots.Clone();
            }
        }

        public IEqualityComparer<T> Comparer => this.comparer;

        public int Count => this.count;

        /// <summary>
        ///     Gets the number of elements the set can hold without growing.
        /// </summary>
        public int Capacity => this.slots.Length == 0 ? 0 : FlatControl.GrowthLimit(this.slots.Length);

        bool ICollection<T>.IsReadOnly => false;

        /// <returns>
        ///     <c>true</c> if it was added, <c>false</c> if it was already there.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Add(T item)
        {
            if (item == null)
            {
                ThrowNullItem();
            }

            var hashCode = this.comparer.GetHashCode(item);
            byte tag;
            var group = FlatControl.Hash(hashCode, this.groupMask, out tag);
            var control = this.control;
            var slots = this.slots;
            for (var step = 1; ; ++step)
            {
                for (var match = FlatControl.Match(control, group, tag); match != 0; match = FlatControl.Next(match))
                {
                    if (this.comparer.Equals(slots[(group << FlatControl.GroupShift) + FlatControl.Lowest(match)].Item, item))
                    {
                        return false;
                    }
                }

                if (FlatControl.MatchEmpty(control, group) != 0)
                {
                    // Not there. When the walk ended in its first group, the first free
                    // slot on the probe sequence is in that group, so the slot is known
                    // without walking again - the common case, and one probe instead of two
                    if (step == 1 && this.growthLeft > 0)
                    {
                        var slot = (group << FlatControl.GroupShift) + FlatControl.Lowest(FlatControl.MatchFree(control, group));
                        if (FlatControl.Get(control, slot) == FlatControl.Empty)
                        {
                            --this.growthLeft;
                        }

                        FlatControl.Set(control, slot, tag);
                        slots[slot].Item = item;
                        ++this.count;
                        ++this.version;
                        return true;
                    }

                    break;
                }

                group = (group + step) & this.groupMask;
            }

            this.Insert(item, hashCode);
            return true;
        }

        /// <summary>
        ///     Removes every element, keeping the capacity.
        /// </summary>
        public void Clear()
        {
            if (this.slots.Length == 0)
            {
                return;
            }

            Array.Clear(this.control, 0, this.control.Length);
            Array.Clear(this.slots, 0, this.slots.Length);
            this.count = 0;
            this.growthLeft = FlatControl.GrowthLimit(this.slots.Length);
            ++this.version;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(T item)
        {
            return this.FindSlot(item) >= 0;
        }

        /// <summary>
        ///     Copies the elements to an array.
        /// </summary>
        public void CopyTo(T[] array)
        {
            this.CopyTo(array, 0, this.count);
        }

        /// <summary>
        ///     Copies the elements to an array.
        /// </summary>
        public void CopyTo(T[] array, int arrayIndex)
        {
            this.CopyTo(array, arrayIndex, this.count);
        }

        /// <summary>
        ///     Copies up to the number of elements given to an array.
        /// </summary>
        public void CopyTo(T[] array, int arrayIndex, int count)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (arrayIndex < 0 || arrayIndex > array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (array.Length - arrayIndex < count)
            {
                throw new ArgumentException("Destination array is not long enough to copy all the items in the collection.");
            }

            var copied = 0;
            var control = this.control;
            var slots = this.slots;
            for (var slot = 0; slot < slots.Length && copied < count; ++slot)
            {
                if (FlatControl.IsFull(FlatControl.Get(control, slot)))
                {
                    array[arrayIndex + copied++] = slots[slot].Item;
                }
            }
        }

        /// <summary>
        ///     Ensures the set can hold the elements given without growing.
        /// </summary>
        /// <returns>
        ///     The capacity afterwards.
        /// </returns>
        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (capacity > this.Capacity)
            {
                this.Resize(FlatControl.SlotsFor(capacity));
            }

            return this.Capacity;
        }

        /// <summary>
        ///     Removes every element that is also in the sequence given.
        /// </summary>
        public void ExceptWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (ReferenceEquals(other, this))
            {
                this.Clear();
                return;
            }

            foreach (var item in other)
            {
                this.Remove(item);
            }
        }

        /// <summary>
        ///     Returns an enumerator over the elements, in slot order.
        /// </summary>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <summary>
        ///     Keeps only the elements that are also in the sequence given.
        /// </summary>
        public void IntersectWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (this.count == 0 || ReferenceEquals(other, this))
            {
                return;
            }

            var keep = this.Found(other);
            if (keep.count == this.count)
            {
                return;
            }

            var control = this.control;
            var slots = this.slots;
            for (var slot = 0; slot < slots.Length; ++slot)
            {
                if (FlatControl.IsFull(FlatControl.Get(control, slot)) && !keep.Contains(slots[slot].Item))
                {
                    this.EraseAt(slot);
                }
            }
        }

        /// <summary>
        ///     Whether this set is a subset of the sequence given and smaller than it.
        /// </summary>
        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (ReferenceEquals(other, this))
            {
                return false;
            }

            var found = new FlatHashSet<T, TComparer>(0, this.comparer);
            var extra = false;
            foreach (var item in other)
            {
                if (this.Contains(item))
                {
                    found.Add(item);
                }
                else
                {
                    extra = true;
                }
            }

            return extra && found.count == this.count;
        }

        /// <summary>
        ///     Whether this set is a superset of the sequence given and larger than it.
        /// </summary>
        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (ReferenceEquals(other, this) || this.count == 0)
            {
                return false;
            }

            var found = new FlatHashSet<T, TComparer>(0, this.comparer);
            foreach (var item in other)
            {
                if (!this.Contains(item))
                {
                    return false;
                }

                found.Add(item);
            }

            return found.count < this.count;
        }

        /// <summary>
        ///     Whether every element is also in the sequence given.
        /// </summary>
        public bool IsSubsetOf(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (this.count == 0 || ReferenceEquals(other, this))
            {
                return true;
            }

            return this.Found(other).count == this.count;
        }

        /// <summary>
        ///     Whether every element of the sequence given is also in this set.
        /// </summary>
        public bool IsSupersetOf(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            foreach (var item in other)
            {
                if (!this.Contains(item))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Whether the set and the sequence given have an element in common.
        /// </summary>
        public bool Overlaps(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (this.count == 0)
            {
                return false;
            }

            if (ReferenceEquals(other, this))
            {
                return true;
            }

            foreach (var item in other)
            {
                if (this.Contains(item))
                {
                    return true;
                }
            }

            return false;
        }

        /// <returns>
        ///     <c>true</c> if it was there.
        /// </returns>
        public bool Remove(T item)
        {
            var slot = this.FindSlot(item);
            if (slot < 0)
            {
                return false;
            }

            this.EraseAt(slot);
            return true;
        }

        /// <summary>
        ///     Removes every element the predicate accepts.
        /// </summary>
        /// <returns>
        ///     The number removed.
        /// </returns>
        public int RemoveWhere(Predicate<T> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException(nameof(match));
            }

            var removed = 0;
            var control = this.control;
            var slots = this.slots;
            for (var slot = 0; slot < slots.Length; ++slot)
            {
                if (FlatControl.IsFull(FlatControl.Get(control, slot)) && match(slots[slot].Item))
                {
                    this.EraseAt(slot);
                    ++removed;
                }
            }

            return removed;
        }

        /// <summary>
        ///     Whether the set and the sequence given hold the same elements.
        /// </summary>
        public bool SetEquals(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (ReferenceEquals(other, this))
            {
                return true;
            }

            var found = new FlatHashSet<T, TComparer>(0, this.comparer);
            foreach (var item in other)
            {
                if (!this.Contains(item))
                {
                    return false;
                }

                found.Add(item);
            }

            return found.count == this.count;
        }

        /// <summary>
        ///     Keeps the elements that are in exactly one of the set and the sequence given.
        /// </summary>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (ReferenceEquals(other, this))
            {
                this.Clear();
                return;
            }

            // the sequence may repeat an element, which must toggle it once, not twice
            var distinct = new FlatHashSet<T, TComparer>(0, this.comparer);
            distinct.UnionWith(other);
            foreach (var item in distinct)
            {
                if (!this.Remove(item))
                {
                    this.Add(item);
                }
            }
        }

        /// <summary>
        ///     Shrinks the table to the smallest that holds what is in it.
        /// </summary>
        public void TrimExcess()
        {
            if (this.count == 0)
            {
                this.control = FlatControl.EmptyGroup;
                this.slots = NoSlots;
                this.groupMask = 0;
                this.growthLeft = 0;
                ++this.version;
                return;
            }

            var slots = FlatControl.SlotsFor(this.count);
            if (slots < this.slots.Length)
            {
                this.Resize(slots);
            }
        }

        /// <summary>
        ///     Finds the element in the set equal to the one given.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if there is one.
        /// </returns>
        public bool TryGetValue(T equalValue, out T actualValue)
        {
            var slot = this.FindSlot(equalValue);
            if (slot >= 0)
            {
                actualValue = this.slots[slot].Item;
                return true;
            }

            actualValue = default(T);
            return false;
        }

        /// <summary>
        ///     Adds every element of the sequence given.
        /// </summary>
        public void UnionWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            foreach (var item in other)
            {
                this.Add(item);
            }
        }

        void ICollection<T>.Add(T item)
        {
            this.Add(item);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new Enumerator(this);
        }

        private static void ThrowNullItem()
        {
            throw new ArgumentNullException("item");
        }

        private void Allocate(int slots)
        {
            this.control = new ulong[slots / 8];
            this.slots = new Slot[slots];
            this.groupMask = (slots >> FlatControl.GroupShift) - 1;
            this.growthLeft = FlatControl.GrowthLimit(slots);
        }

        private void EraseAt(int slot)
        {
            // a group that still has an empty slot was never probed past, see FlatControl
            if (FlatControl.MatchEmpty(this.control, slot >> FlatControl.GroupShift) != 0)
            {
                FlatControl.Set(this.control, slot, FlatControl.Empty);
                ++this.growthLeft;
            }
            else
            {
                FlatControl.Set(this.control, slot, FlatControl.Deleted);
            }

            this.slots[slot].Item = default(T);
            --this.count;
        }

        private int FindFree(int hashCode, out byte tag)
        {
            var group = FlatControl.Hash(hashCode, this.groupMask, out tag);
            for (var step = 1; ; ++step)
            {
                var free = FlatControl.MatchFree(this.control, group);
                if (free != 0)
                {
                    return (group << FlatControl.GroupShift) + FlatControl.Lowest(free);
                }

                group = (group + step) & this.groupMask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindSlot(T item)
        {
            if (item == null)
            {
                ThrowNullItem();
            }

            byte tag;
            var group = FlatControl.Hash(this.comparer.GetHashCode(item), this.groupMask, out tag);
            var control = this.control;
            var slots = this.slots;
            for (var step = 1; ; ++step)
            {
                for (var match = FlatControl.Match(control, group, tag); match != 0; match = FlatControl.Next(match))
                {
                    var slot = (group << FlatControl.GroupShift) + FlatControl.Lowest(match);
                    if (this.comparer.Equals(slots[slot].Item, item))
                    {
                        return slot;
                    }
                }

                if (FlatControl.MatchEmpty(control, group) != 0)
                {
                    return -1;
                }

                group = (group + step) & this.groupMask;
            }
        }

        /// <summary>
        ///     The distinct elements of the sequence given that are also in this set.
        /// </summary>
        private FlatHashSet<T, TComparer> Found(IEnumerable<T> other)
        {
            var found = new FlatHashSet<T, TComparer>(0, this.comparer);
            foreach (var item in other)
            {
                if (this.Contains(item))
                {
                    found.Add(item);
                }
            }

            return found;
        }

        /// <summary>
        ///     Claims a slot for an element known not to be there. Out of line, see the
        ///     dictionary's.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Insert(T item, int hashCode)
        {
            byte tag;
            var slot = this.FindFree(hashCode, out tag);
            if (FlatControl.Get(this.control, slot) == FlatControl.Empty)
            {
                if (this.growthLeft == 0)
                {
                    var slots = Math.Max(this.slots.Length, FlatControl.GroupWidth);
                    if (this.count >= FlatControl.GrowthLimit(slots) / 2)
                    {
                        if (slots >= FlatControl.MaxSlots)
                        {
                            throw new OutOfMemoryException();
                        }

                        slots = FlatControl.GrownSlots(slots);
                    }

                    this.Resize(slots);
                    slot = this.FindFree(hashCode, out tag);
                }

                --this.growthLeft;
            }

            FlatControl.Set(this.control, slot, tag);
            this.slots[slot].Item = item;
            ++this.count;
            ++this.version;
        }

        /// <summary>
        ///     Rebuilds the table at the slots given, dropping the tombstones.
        /// </summary>
        /// <remarks>
        ///     Everything in locals and the free slot searched inline: this runs once per
        ///     element per growth, and a set grown from empty grows several times. The new
        ///     table has no tombstones, so a free slot is an empty one.
        /// </remarks>
        private void Resize(int slots)
        {
            var oldControl = this.control;
            var oldSlots = this.slots;
            this.Allocate(slots);

            var control = this.control;
            var newSlots = this.slots;
            var groupMask = this.groupMask;
            var groups = oldSlots.Length >> FlatControl.GroupShift;
            for (var oldGroup = 0; oldGroup < groups; ++oldGroup)
            {
                for (var match = FlatControl.MatchFull(oldControl, oldGroup); match != 0; match = FlatControl.Next(match))
                {
                    var item = oldSlots[(oldGroup << FlatControl.GroupShift) + FlatControl.Lowest(match)].Item;
                    byte tag;
                    var group = FlatControl.Hash(this.comparer.GetHashCode(item), groupMask, out tag);
                    ulong free;
                    for (var step = 1; (free = FlatControl.MatchFree(control, group)) == 0; ++step)
                    {
                        group = (group + step) & groupMask;
                    }

                    var slot = (group << FlatControl.GroupShift) + FlatControl.Lowest(free);
                    FlatControl.Set(control, slot, tag);
                    newSlots[slot].Item = item;
                }
            }

            this.growthLeft -= this.count;
            ++this.version;
        }

        /// <summary>
        ///     One element. A struct around it so that storing one is a field store and not
        ///     an array element store, see <see cref="slots" />.
        /// </summary>
        internal struct Slot
        {
            internal T Item;
        }

        /// <summary>
        ///     Enumerates the elements.
        /// </summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly FlatHashSet<T, TComparer> set;

            private readonly int version;

            private int slot;

            private T current;

            internal Enumerator(FlatHashSet<T, TComparer> set)
            {
                this.set = set;
                this.version = set.version;
                this.slot = -1;
                this.current = default(T);
            }

            public T Current => this.current;

            object IEnumerator.Current => this.current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                var s = this.set;
                if (this.version != s.version)
                {
                    ThrowVersionChanged();
                }

                var slots = s.slots;
                while (++this.slot < slots.Length)
                {
                    if (FlatControl.IsFull(FlatControl.Get(s.control, this.slot)))
                    {
                        this.current = slots[this.slot].Item;
                        return true;
                    }
                }

                this.current = default(T);
                return false;
            }

            public void Reset()
            {
                if (this.version != this.set.version)
                {
                    ThrowVersionChanged();
                }

                this.slot = -1;
                this.current = default(T);
            }

            private static void ThrowVersionChanged()
            {
                throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
            }
        }
    }
}
