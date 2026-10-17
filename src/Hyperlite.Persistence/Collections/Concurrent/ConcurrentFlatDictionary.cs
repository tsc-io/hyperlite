namespace Hyperlite.Persistence.Collections.Concurrent
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;

    /// <summary>
    ///     A thread safe map for data that is written rarely and read constantly: a
    ///     copy-on-write <see cref="FlatDictionary{TKey, TValue, TComparer}" />.
    /// </summary>
    /// <typeparam name="TKey">
    ///     The key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The value type.
    /// </typeparam>
    /// <typeparam name="TComparer">
    ///     The comparer type, a struct as for the flat dictionary.
    /// </typeparam>
    /// <remarks>
    ///     <para>
    ///     A read loads the current table through one volatile field and looks the key up
    ///     in it, with no lock and no interlocked instruction. That is safe because a
    ///     table, once published, is never written again: a writer takes the lock, copies
    ///     the table slot for slot, changes the copy and publishes it. A reader therefore
    ///     sees either the table before a write or the one after, never one in between.
    ///     </para>
    ///     <para>
    ///     <b>A write costs a copy of the whole table</b>, so this is for caches that are
    ///     filled once per key and read from then on - a Type-keyed cache in a library that
    ///     meets a few hundred types, say. It is the wrong shape for a map written once per
    ///     entity; that is what the framework's <c>ConcurrentDictionary</c> is for.
    ///     </para>
    ///     <para>
    ///     Otherwise it keeps the framework's semantics where the library relies on them:
    ///     a <see cref="GetOrAdd(TKey, Func{TKey, TValue})" /> factory runs outside the
    ///     lock, so it may run more than once for one key under a race and exactly one
    ///     result wins; and enumeration, <see cref="Keys" /> and <see cref="Values" /> see a
    ///     snapshot. The members are the ones the library calls on its concurrent caches,
    ///     including the closure-free <c>GetOrAdd</c> overloads and an <c>AddOrUpdate</c>
    ///     that says whether it added, which it used to get from
    ///     <c>Extensions.DictionaryExtensions</c>.
    ///     </para>
    /// </remarks>
    [DebuggerDisplay("Count = {Count}")]
    internal class ConcurrentFlatDictionary<TKey, TValue, TComparer> : IEnumerable<KeyValuePair<TKey, TValue>>
        where TComparer : struct, IEqualityComparer<TKey>
    {
        private readonly object writeLock = new object();

        /// <summary>
        ///     The current table. Never written after it is published.
        /// </summary>
        private volatile FlatDictionary<TKey, TValue, TComparer> table;

        public ConcurrentFlatDictionary()
            : this(0)
        {
        }

        /// <param name="capacity">
        ///     The number of entries the first table holds before a write has to grow it.
        /// </param>
        public ConcurrentFlatDictionary(int capacity)
        {
            this.table = new FlatDictionary<TKey, TValue, TComparer>(capacity);
        }

        /// <param name="collection">
        ///     The entries to copy; a key that repeats keeps its last value.
        /// </param>
        public ConcurrentFlatDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            var initial = new FlatDictionary<TKey, TValue, TComparer>(
                collection is ICollection<KeyValuePair<TKey, TValue>> c ? c.Count : 0);
            foreach (var pair in collection)
            {
                initial[pair.Key] = pair.Value;
            }

            this.table = initial;
        }

        public int Count => this.table.Count;

        /// <summary>
        ///     Gets a value indicating whether there are no entries.
        /// </summary>
        public bool IsEmpty => this.table.Count == 0;

        /// <summary>
        ///     Gets a snapshot of the keys.
        /// </summary>
        /// <remarks>
        ///     Enumerated from the table directly, not through its <c>Keys</c>: that property
        ///     creates its collection lazily, which would be a write to a table that is never
        ///     supposed to be written after it is published.
        /// </remarks>
        public ICollection<TKey> Keys
        {
            get
            {
                var table = this.table;
                var keys = new List<TKey>(table.Count);
                foreach (var entry in table)
                {
                    keys.Add(entry.Key);
                }

                return keys;
            }
        }

        /// <summary>
        ///     Gets a snapshot of the values.
        /// </summary>
        /// <remarks>
        ///     Enumerated from the table directly, for the reason <see cref="Keys" /> is.
        /// </remarks>
        public ICollection<TValue> Values
        {
            get
            {
                var table = this.table;
                var values = new List<TValue>(table.Count);
                foreach (var entry in table)
                {
                    values.Add(entry.Value);
                }

                return values;
            }
        }

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
                return this.table[key];
            }

            set
            {
                this.AddOrUpdate(key, value);
            }
        }

        /// <summary>
        ///     Sets the value for a key, adding the key if it is not there.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the key was added, <c>false</c> if its value was replaced.
        /// </returns>
        public bool AddOrUpdate(TKey key, TValue value)
        {
            lock (this.writeLock)
            {
                var next = new FlatDictionary<TKey, TValue, TComparer>(this.table);
                bool exists;
                next.GetValueRefOrAddDefault(key, out exists) = value;
                this.table = next;
                return !exists;
            }
        }

        /// <summary>
        ///     Removes every entry.
        /// </summary>
        public void Clear()
        {
            lock (this.writeLock)
            {
                this.table = new FlatDictionary<TKey, TValue, TComparer>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(TKey key)
        {
            return this.table.ContainsKey(key);
        }

        /// <summary>
        ///     Returns an enumerator over a snapshot of the entries.
        /// </summary>
        public FlatDictionary<TKey, TValue, TComparer>.Enumerator GetEnumerator()
        {
            return this.table.GetEnumerator();
        }

        /// <summary>
        ///     The value for the key, adding the one given first if the key is not there.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetOrAdd(TKey key, TValue value)
        {
            TValue existing;
            return this.table.TryGetValue(key, out existing) ? existing : this.Add(key, value);
        }

        /// <summary>
        ///     The value for the key, calling the factory for it first if the key is not there.
        /// </summary>
        /// <remarks>
        ///     The factory runs outside the lock: it may run more than once for a key when
        ///     two threads miss together, and one result wins, as with the framework's.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
        {
            TValue existing;
            return this.table.TryGetValue(key, out existing) ? existing : this.Add(key, factory(key));
        }

        /// <summary>
        ///     The value for the key, calling the factory with the argument given first if the
        ///     key is not there - the closure-free form.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetOrAdd<TArg>(TKey key, TArg argument, Func<TKey, TArg, TValue> factory)
        {
            TValue existing;
            return this.table.TryGetValue(key, out existing) ? existing : this.Add(key, factory(key, argument));
        }

        /// <summary>
        ///     The value for the key, calling the factory with the arguments given first if the
        ///     key is not there - the closure-free form.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetOrAdd<TArg1, TArg2>(TKey key, TArg1 first, TArg2 second, Func<TKey, TArg1, TArg2, TValue> factory)
        {
            TValue existing;
            return this.table.TryGetValue(key, out existing) ? existing : this.Add(key, factory(key, first, second));
        }

        /// <summary>
        ///     Removes the key.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if it was there.
        /// </returns>
        public bool Remove(TKey key)
        {
            TValue value;
            return this.TryRemove(key, out value);
        }

        /// <summary>
        ///     Adds an entry if the key is not already there.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if it was added.
        /// </returns>
        public bool TryAdd(TKey key, TValue value)
        {
            if (this.table.ContainsKey(key))
            {
                return false;
            }

            lock (this.writeLock)
            {
                var current = this.table;
                if (current.ContainsKey(key))
                {
                    return false;
                }

                var next = new FlatDictionary<TKey, TValue, TComparer>(current);
                next.Add(key, value);
                this.table = next;
                return true;
            }
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
            return this.table.TryGetValue(key, out value);
        }

        /// <summary>
        ///     Removes the key, handing back what was there.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if it was there.
        /// </returns>
        public bool TryRemove(TKey key, out TValue value)
        {
            if (!this.table.ContainsKey(key))
            {
                value = default(TValue);
                return false;
            }

            lock (this.writeLock)
            {
                var next = new FlatDictionary<TKey, TValue, TComparer>(this.table);
                if (!next.Remove(key, out value))
                {
                    return false;
                }

                this.table = next;
                return true;
            }
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return this.table.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.table.GetEnumerator();
        }

        /// <summary>
        ///     Adds a value computed outside the lock, unless another thread added one first,
        ///     in which case that one wins and is returned.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private TValue Add(TKey key, TValue value)
        {
            lock (this.writeLock)
            {
                var current = this.table;
                TValue existing;
                if (current.TryGetValue(key, out existing))
                {
                    return existing;
                }

                var next = new FlatDictionary<TKey, TValue, TComparer>(current);
                next.Add(key, value);
                this.table = next;
                return value;
            }
        }
    }
}
