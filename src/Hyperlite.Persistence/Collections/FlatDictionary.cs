namespace Hyperlite.Persistence.Collections
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;

    /// <summary>
    ///     A flat, open addressed hash map in the manner of Abseil's Swiss table, with the
    ///     comparer as a struct type argument.
    /// </summary>
    /// <typeparam name="TKey">
    ///     The key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The value type.
    /// </typeparam>
    /// <typeparam name="TComparer">
    ///     The comparer type. A struct, so the JIT specialises the table for it and inlines
    ///     the hash and the equality test, where the framework's dictionary makes an
    ///     interface call for each.
    /// </typeparam>
    /// <remarks>
    ///     Meant as a drop-in for <see cref="Dictionary{TKey, TValue}" />: the same
    ///     members with the same semantics, including the exceptions, plus the single probe
    ///     operations the framework lacks on net48 - <see cref="TryGetOrAddValue" />,
    ///     <see cref="GetOrAdd(TKey, TValue)" /> and <see cref="GetValueRefOrAddDefault" />.
    ///     <para>
    ///     One difference a caller can see: <b>the enumeration order is the slot order,
    ///     not the insertion order.</b> The framework's dictionary happens to enumerate in
    ///     insertion order until the first removal, and code can come to depend on that
    ///     without saying so. Do not swap this in where anything is enumerated into an
    ///     order that matters - a type schema, a column list, a wire format.
    ///     </para>
    ///     <para>
    ///     Removing during enumeration is allowed, as it is on .NET Core 3.0 and later: no
    ///     slot moves when another is erased.
    ///     </para>
    ///     <para>
    ///     The layout is two arrays: the control words, see <see cref="FlatControl" />, and
    ///     the entries, a key and a value each and nothing else - no stored hash and no
    ///     chain link, so an entry of an object key and an int value is 16 bytes where the
    ///     framework's is 24.
    ///     </para>
    /// </remarks>
    [DebuggerDisplay("Count = {Count}")]
    internal class FlatDictionary<TKey, TValue, TComparer> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
        where TComparer : struct, IEqualityComparer<TKey>
    {
        private static readonly Entry[] NoEntries = new Entry[0];

        /// <summary>
        ///     The comparer. Not readonly: a readonly struct field is copied before every
        ///     call made on it.
        /// </summary>
        private TComparer comparer;

        private ulong[] control;

        private Entry[] entries;

        /// <summary>
        ///     The group count less one; the group count is a power of two.
        /// </summary>
        private int groupMask;

        private int count;

        /// <summary>
        ///     Inserts left into an empty slot before the table has to grow. A tombstone
        ///     keeps its share of this, which is what makes a table rebuild once removals
        ///     have filled it with them.
        /// </summary>
        private int growthLeft;

        private int version;

        private KeyCollection keys;

        private ValueCollection values;

        public FlatDictionary()
            : this(0, default(TComparer))
        {
        }

        /// <param name="capacity">
        ///     The number of entries the dictionary can hold without growing.
        /// </param>
        public FlatDictionary(int capacity)
            : this(capacity, default(TComparer))
        {
        }

        /// <param name="comparer">
        ///     The comparer, for a comparer struct that carries state.
        /// </param>
        public FlatDictionary(TComparer comparer)
            : this(0, comparer)
        {
        }

        /// <param name="capacity">
        ///     The number of entries the dictionary can hold without growing.
        /// </param>
        /// <param name="comparer">
        ///     The comparer, for a comparer struct that carries state.
        /// </param>
        public FlatDictionary(int capacity, TComparer comparer)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            this.comparer = comparer;
            if (capacity == 0)
            {
                this.control = FlatControl.EmptyGroup;
                this.entries = NoEntries;
            }
            else
            {
                this.Allocate(FlatControl.SlotsFor(capacity));
            }
        }

        /// <param name="collection">
        ///     The entries to copy.
        /// </param>
        public FlatDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection)
            : this(collection is ICollection<KeyValuePair<TKey, TValue>> c ? c.Count : 0, default(TComparer))
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            foreach (var pair in collection)
            {
                this.Add(pair.Key, pair.Value);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FlatDictionary{TKey, TValue, TComparer}" /> class
        ///     as a copy of another, slot for slot.
        /// </summary>
        /// <remarks>
        ///     The arrays are copied as they are rather than rehashed, which is what makes a
        ///     copy-on-write write cost two array copies - see
        ///     <see cref="Concurrent.ConcurrentFlatDictionary{TKey, TValue, TComparer}" />.
        /// </remarks>
        internal FlatDictionary(FlatDictionary<TKey, TValue, TComparer> source)
        {
            this.comparer = source.comparer;
            this.groupMask = source.groupMask;
            this.count = source.count;
            this.growthLeft = source.growthLeft;
            if (source.entries.Length == 0)
            {
                this.control = FlatControl.EmptyGroup;
                this.entries = NoEntries;
            }
            else
            {
                this.control = (ulong[])source.control.Clone();
                this.entries = (Entry[])source.entries.Clone();
            }
        }

        public IEqualityComparer<TKey> Comparer => this.comparer;

        public int Count => this.count;

        /// <summary>
        ///     Gets the number of entries the dictionary can hold without growing.
        /// </summary>
        public int Capacity => this.entries.Length == 0 ? 0 : FlatControl.GrowthLimit(this.entries.Length);

        public KeyCollection Keys => this.keys ?? (this.keys = new KeyCollection(this));

        public ValueCollection Values => this.values ?? (this.values = new ValueCollection(this));

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => this.Keys;

        ICollection<TValue> IDictionary<TKey, TValue>.Values => this.Values;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => this.Keys;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => this.Values;

        /// <summary>
        ///     Gets or sets the value for a key.
        /// </summary>
        /// <exception cref="KeyNotFoundException">
        ///     On a get, if the key is not there.
        /// </exception>
        public TValue this[TKey key]
        {
            get
            {
                var slot = this.FindSlot(key);
                if (slot < 0)
                {
                    ThrowKeyNotFound(key);
                }

                return this.entries[slot].Value;
            }

            set
            {
                bool exists;
                var slot = this.FindOrInsert(key, out exists);
                this.entries[slot].Value = value;
            }
        }

        /// <exception cref="ArgumentException">
        ///     If the key is already there.
        /// </exception>
        public void Add(TKey key, TValue value)
        {
            bool exists;
            var slot = this.FindOrInsert(key, out exists);
            if (exists)
            {
                ThrowDuplicateKey(key);
            }

            this.entries[slot].Value = value;
        }

        /// <summary>
        ///     Removes every entry, keeping the capacity.
        /// </summary>
        public void Clear()
        {
            if (this.entries.Length == 0)
            {
                return;
            }

            Array.Clear(this.control, 0, this.control.Length);
            Array.Clear(this.entries, 0, this.entries.Length);
            this.count = 0;
            this.growthLeft = FlatControl.GrowthLimit(this.entries.Length);
            ++this.version;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(TKey key)
        {
            return this.FindSlot(key) >= 0;
        }

        /// <summary>
        ///     Whether any entry has the value given. A linear scan.
        /// </summary>
        public bool ContainsValue(TValue value)
        {
            var valueComparer = System.Collections.Generic.EqualityComparer<TValue>.Default;
            var control = this.control;
            var entries = this.entries;
            for (var slot = 0; slot < entries.Length; ++slot)
            {
                if (FlatControl.IsFull(FlatControl.Get(control, slot)) && valueComparer.Equals(entries[slot].Value, value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Ensures the dictionary can hold the entries given without growing.
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
        ///     Returns an enumerator over the entries, in slot order.
        /// </summary>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <summary>
        ///     The value for the key, adding the one given first if the key is not there.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetOrAdd(TKey key, TValue value)
        {
            bool exists;
            var slot = this.FindOrInsert(key, out exists);
            if (exists)
            {
                return this.entries[slot].Value;
            }

            this.entries[slot].Value = value;
            return value;
        }

        /// <summary>
        ///     The value for the key, calling the factory for it first if the key is not there.
        /// </summary>
        /// <remarks>
        ///     Two probes on a miss rather than one: the factory runs before the slot is
        ///     claimed, so a factory that throws, or that touches this dictionary, cannot
        ///     leave it holding a key with no value.
        /// </remarks>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
        {
            var slot = this.FindSlot(key);
            if (slot >= 0)
            {
                return this.entries[slot].Value;
            }

            var value = factory(key);
            bool exists;
            slot = this.FindOrInsert(key, out exists);
            this.entries[slot].Value = value;
            return value;
        }

        /// <summary>
        ///     A reference to the value for the key, adding the key with a default value first
        ///     if it is not there.
        /// </summary>
        /// <remarks>
        ///     The reference is good until the next insert, which may move every entry.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref TValue GetValueRefOrAddDefault(TKey key, out bool exists)
        {
            var slot = this.FindOrInsert(key, out exists);
            return ref this.entries[slot].Value;
        }

        /// <summary>
        ///     Removes the key.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if it was there.
        /// </returns>
        public bool Remove(TKey key)
        {
            var slot = this.FindSlot(key);
            if (slot < 0)
            {
                return false;
            }

            this.EraseAt(slot);
            return true;
        }

        /// <summary>
        ///     Removes the key, handing back what was there.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if it was there.
        /// </returns>
        public bool Remove(TKey key, out TValue value)
        {
            var slot = this.FindSlot(key);
            if (slot < 0)
            {
                value = default(TValue);
                return false;
            }

            value = this.entries[slot].Value;
            this.EraseAt(slot);
            return true;
        }

        /// <summary>
        ///     Shrinks the table to the smallest that holds what is in it.
        /// </summary>
        public void TrimExcess()
        {
            this.TrimExcess(this.count);
        }

        /// <summary>
        ///     Shrinks the table to the smallest that holds the entries given.
        /// </summary>
        public void TrimExcess(int capacity)
        {
            if (capacity < this.count)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (capacity == 0)
            {
                this.control = FlatControl.EmptyGroup;
                this.entries = NoEntries;
                this.groupMask = 0;
                this.growthLeft = 0;
                ++this.version;
                return;
            }

            var slots = FlatControl.SlotsFor(capacity);
            if (slots < this.entries.Length)
            {
                this.Resize(slots);
            }
        }

        /// <summary>
        ///     Adds an entry if the key is not already there.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if it was added.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAdd(TKey key, TValue value)
        {
            bool exists;
            var slot = this.FindOrInsert(key, out exists);
            if (exists)
            {
                return false;
            }

            this.entries[slot].Value = value;
            return true;
        }

        /// <summary>
        ///     The value for the key if it is there; otherwise adds the one given and says so.
        ///     One probe either way.
        /// </summary>
        /// <returns>
        ///     <c>true</c> when the key was already present and <paramref name="value" /> is
        ///     what was found, <c>false</c> when <paramref name="toAdd" /> was added.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetOrAddValue(TKey key, out TValue value, TValue toAdd)
        {
            bool exists;
            var slot = this.FindOrInsert(key, out exists);
            if (exists)
            {
                value = this.entries[slot].Value;
                return true;
            }

            this.entries[slot].Value = toAdd;
            value = toAdd;
            return false;
        }

        /// <summary>
        ///     Gets the value for a key.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the key is there.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(TKey key, out TValue value)
        {
            var slot = this.FindSlot(key);
            if (slot >= 0)
            {
                value = this.entries[slot].Value;
                return true;
            }

            value = default(TValue);
            return false;
        }

        /// <summary>
        ///     Removes the key, handing back what was there.
        /// </summary>
        public bool TryRemove(TKey key, out TValue value)
        {
            return this.Remove(key, out value);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            this.Add(item.Key, item.Value);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            var slot = this.FindSlot(item.Key);
            return slot >= 0 && System.Collections.Generic.EqualityComparer<TValue>.Default.Equals(this.entries[slot].Value, item.Value);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            CheckCopyTo(array, arrayIndex, this.count);
            foreach (var pair in this)
            {
                array[arrayIndex++] = pair;
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            var slot = this.FindSlot(item.Key);
            if (slot < 0 || !System.Collections.Generic.EqualityComparer<TValue>.Default.Equals(this.entries[slot].Value, item.Value))
            {
                return false;
            }

            this.EraseAt(slot);
            return true;
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new Enumerator(this);
        }

        private static void CheckCopyTo(Array array, int arrayIndex, int count)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (arrayIndex < 0 || arrayIndex > array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            }

            if (array.Length - arrayIndex < count)
            {
                throw new ArgumentException("Destination array is not long enough to copy all the items in the collection.");
            }
        }

        private static void ThrowDuplicateKey(TKey key)
        {
            throw new ArgumentException("An item with the same key has already been added. Key: " + key, nameof(key));
        }

        private static void ThrowKeyNotFound(TKey key)
        {
            throw new KeyNotFoundException("The given key '" + key + "' was not present in the dictionary.");
        }

        private static void ThrowNullKey()
        {
            throw new ArgumentNullException("key");
        }

        private void Allocate(int slots)
        {
            this.control = new ulong[slots / 8];
            this.entries = new Entry[slots];
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

            this.entries[slot] = default(Entry);
            --this.count;
        }

        /// <summary>
        ///     The first free slot on the probe sequence of a hash, in a table known to have
        ///     room and not to hold the key.
        /// </summary>
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

        /// <summary>
        ///     The slot holding the key, adding the key with a default value if it is not there.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindOrInsert(TKey key, out bool exists)
        {
            if (key == null)
            {
                ThrowNullKey();
            }

            var hashCode = this.comparer.GetHashCode(key);
            byte tag;
            var group = FlatControl.Hash(hashCode, this.groupMask, out tag);
            var control = this.control;
            var entries = this.entries;
            for (var step = 1; ; ++step)
            {
                for (var match = FlatControl.Match(control, group, tag); match != 0; match = FlatControl.Next(match))
                {
                    var slot = (group << FlatControl.GroupShift) + FlatControl.Lowest(match);
                    if (this.comparer.Equals(entries[slot].Key, key))
                    {
                        exists = true;
                        return slot;
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
                        entries[slot].Key = key;
                        ++this.count;
                        ++this.version;
                        exists = false;
                        return slot;
                    }

                    break;
                }

                group = (group + step) & this.groupMask;
            }

            exists = false;
            return this.Insert(key, hashCode);
        }

        /// <summary>
        ///     The slot holding the key, or -1.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindSlot(TKey key)
        {
            if (key == null)
            {
                ThrowNullKey();
            }

            byte tag;
            var group = FlatControl.Hash(this.comparer.GetHashCode(key), this.groupMask, out tag);
            var control = this.control;
            var entries = this.entries;
            for (var step = 1; ; ++step)
            {
                for (var match = FlatControl.Match(control, group, tag); match != 0; match = FlatControl.Next(match))
                {
                    var slot = (group << FlatControl.GroupShift) + FlatControl.Lowest(match);
                    if (this.comparer.Equals(entries[slot].Key, key))
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
        ///     Claims a slot for a key known not to be there.
        /// </summary>
        /// <remarks>
        ///     The slow path of <see cref="FindOrInsert" />, kept out of line: the walk ran
        ///     past its first group, or the table has to grow.
        ///     <para>
        ///     The slot is found by a second walk of the probe sequence rather than
        ///     remembered during the first, so the hit path never pays to track a free slot
        ///     it will not use. When the first walk ended in its first group the caller
        ///     already knows the slot and does not come here.
        ///     </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private int Insert(TKey key, int hashCode)
        {
            byte tag;
            var slot = this.FindFree(hashCode, out tag);
            if (FlatControl.Get(this.control, slot) == FlatControl.Empty)
            {
                if (this.growthLeft == 0)
                {
                    this.Grow();
                    slot = this.FindFree(hashCode, out tag);
                }

                --this.growthLeft;
            }

            FlatControl.Set(this.control, slot, tag);
            this.entries[slot].Key = key;
            ++this.count;
            ++this.version;
            return slot;
        }

        /// <summary>
        ///     Makes room for one more entry: twice the slots, or the same number again if
        ///     tombstones rather than entries are what filled the table.
        /// </summary>
        private void Grow()
        {
            var slots = Math.Max(this.entries.Length, FlatControl.GroupWidth);
            if (this.count >= FlatControl.GrowthLimit(slots) / 2)
            {
                if (slots >= FlatControl.MaxSlots)
                {
                    throw new OutOfMemoryException();
                }

                slots = FlatControl.GrownSlots(slots);
            }

            this.Resize(slots);
        }

        /// <summary>
        ///     Rebuilds the table at the slots given, dropping the tombstones.
        /// </summary>
        /// <remarks>
        ///     Everything in locals and the free slot searched inline: this runs once per
        ///     entry per growth. The new table has no tombstones, so a free slot is an
        ///     empty one.
        /// </remarks>
        private void Resize(int slots)
        {
            var oldControl = this.control;
            var oldEntries = this.entries;
            this.Allocate(slots);

            var control = this.control;
            var entries = this.entries;
            var groupMask = this.groupMask;
            var groups = oldEntries.Length >> FlatControl.GroupShift;
            for (var oldGroup = 0; oldGroup < groups; ++oldGroup)
            {
                for (var match = FlatControl.MatchFull(oldControl, oldGroup); match != 0; match = FlatControl.Next(match))
                {
                    var oldSlot = (oldGroup << FlatControl.GroupShift) + FlatControl.Lowest(match);
                    byte tag;
                    var group = FlatControl.Hash(this.comparer.GetHashCode(oldEntries[oldSlot].Key), groupMask, out tag);
                    ulong free;
                    for (var step = 1; (free = FlatControl.MatchFree(control, group)) == 0; ++step)
                    {
                        group = (group + step) & groupMask;
                    }

                    var slot = (group << FlatControl.GroupShift) + FlatControl.Lowest(free);
                    FlatControl.Set(control, slot, tag);
                    entries[slot] = oldEntries[oldSlot];
                }
            }

            this.growthLeft -= this.count;
            ++this.version;
        }

        /// <summary>
        ///     A slot's key and value.
        /// </summary>
        internal struct Entry
        {
            internal TKey Key;

            internal TValue Value;
        }

        /// <summary>
        ///     Enumerates the entries.
        /// </summary>
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly FlatDictionary<TKey, TValue, TComparer> dictionary;

            private readonly int version;

            private int slot;

            private KeyValuePair<TKey, TValue> current;

            internal Enumerator(FlatDictionary<TKey, TValue, TComparer> dictionary)
            {
                this.dictionary = dictionary;
                this.version = dictionary.version;
                this.slot = -1;
                this.current = default(KeyValuePair<TKey, TValue>);
            }

            public KeyValuePair<TKey, TValue> Current => this.current;

            object IEnumerator.Current => this.current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                var d = this.dictionary;
                if (this.version != d.version)
                {
                    ThrowVersionChanged();
                }

                var entries = d.entries;
                while (++this.slot < entries.Length)
                {
                    if (FlatControl.IsFull(FlatControl.Get(d.control, this.slot)))
                    {
                        this.current = new KeyValuePair<TKey, TValue>(entries[this.slot].Key, entries[this.slot].Value);
                        return true;
                    }
                }

                this.current = default(KeyValuePair<TKey, TValue>);
                return false;
            }

            public void Reset()
            {
                if (this.version != this.dictionary.version)
                {
                    ThrowVersionChanged();
                }

                this.slot = -1;
                this.current = default(KeyValuePair<TKey, TValue>);
            }

            internal static void ThrowVersionChanged()
            {
                throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
            }
        }

        /// <summary>
        ///     The keys, as a live view.
        /// </summary>
        [DebuggerDisplay("Count = {Count}")]
        public sealed class KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
        {
            private readonly FlatDictionary<TKey, TValue, TComparer> dictionary;

            internal KeyCollection(FlatDictionary<TKey, TValue, TComparer> dictionary)
            {
                this.dictionary = dictionary;
            }

            public int Count => this.dictionary.count;

            bool ICollection<TKey>.IsReadOnly => true;

            public bool Contains(TKey item)
            {
                return this.dictionary.ContainsKey(item);
            }

            public void CopyTo(TKey[] array, int arrayIndex)
            {
                CheckCopyTo(array, arrayIndex, this.dictionary.count);
                foreach (var key in this)
                {
                    array[arrayIndex++] = key;
                }
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(this.dictionary);
            }

            void ICollection<TKey>.Add(TKey item)
            {
                throw new NotSupportedException();
            }

            void ICollection<TKey>.Clear()
            {
                throw new NotSupportedException();
            }

            bool ICollection<TKey>.Remove(TKey item)
            {
                throw new NotSupportedException();
            }

            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator()
            {
                return new Enumerator(this.dictionary);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new Enumerator(this.dictionary);
            }

            /// <summary>
            ///     Enumerates the keys.
            /// </summary>
            public struct Enumerator : IEnumerator<TKey>
            {
                private FlatDictionary<TKey, TValue, TComparer>.Enumerator inner;

                internal Enumerator(FlatDictionary<TKey, TValue, TComparer> dictionary)
                {
                    this.inner = new FlatDictionary<TKey, TValue, TComparer>.Enumerator(dictionary);
                }

                public TKey Current => this.inner.Current.Key;

                object IEnumerator.Current => this.inner.Current.Key;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    return this.inner.MoveNext();
                }

                public void Reset()
                {
                    this.inner.Reset();
                }
            }
        }

        /// <summary>
        ///     The values, as a live view.
        /// </summary>
        [DebuggerDisplay("Count = {Count}")]
        public sealed class ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
        {
            private readonly FlatDictionary<TKey, TValue, TComparer> dictionary;

            internal ValueCollection(FlatDictionary<TKey, TValue, TComparer> dictionary)
            {
                this.dictionary = dictionary;
            }

            public int Count => this.dictionary.count;

            bool ICollection<TValue>.IsReadOnly => true;

            public bool Contains(TValue item)
            {
                return this.dictionary.ContainsValue(item);
            }

            public void CopyTo(TValue[] array, int arrayIndex)
            {
                CheckCopyTo(array, arrayIndex, this.dictionary.count);
                foreach (var value in this)
                {
                    array[arrayIndex++] = value;
                }
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(this.dictionary);
            }

            void ICollection<TValue>.Add(TValue item)
            {
                throw new NotSupportedException();
            }

            void ICollection<TValue>.Clear()
            {
                throw new NotSupportedException();
            }

            bool ICollection<TValue>.Remove(TValue item)
            {
                throw new NotSupportedException();
            }

            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
            {
                return new Enumerator(this.dictionary);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new Enumerator(this.dictionary);
            }

            /// <summary>
            ///     Enumerates the values.
            /// </summary>
            public struct Enumerator : IEnumerator<TValue>
            {
                private FlatDictionary<TKey, TValue, TComparer>.Enumerator inner;

                internal Enumerator(FlatDictionary<TKey, TValue, TComparer> dictionary)
                {
                    this.inner = new FlatDictionary<TKey, TValue, TComparer>.Enumerator(dictionary);
                }

                public TValue Current => this.inner.Current.Value;

                object IEnumerator.Current => this.inner.Current.Value;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    return this.inner.MoveNext();
                }

                public void Reset()
                {
                    this.inner.Reset();
                }
            }
        }
    }
}
