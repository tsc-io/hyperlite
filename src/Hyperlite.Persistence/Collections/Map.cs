namespace Hyperlite.Persistence.Collections
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Hyperlite.Persistence.Extensions;

    /// <summary>
    ///     Represents a collection of key-value pairs.
    /// </summary>
    /// <typeparam name="TKey">
    ///     The key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The value type.
    /// </typeparam>
    internal sealed class Map<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
        where TKey : IEquatable<TKey>
    {
        private readonly Dictionary<TKey, TValue> dictionary = new Dictionary<TKey, TValue>(16);

        private TKey recentKey;

        private TValue recentValue;

        /// <summary>
        ///     Returns an enumerator that iterates through the map.
        /// </summary>
        /// <returns>
        ///     An enumerator for the map.
        /// </returns>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return this.dictionary.GetEnumerator();
        }

        /// <summary>
        ///     Returns an enumerator that iterates through the map.
        /// </summary>
        /// <returns>
        ///     An enumerator for the map.
        /// </returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.dictionary.GetEnumerator();
        }

        /// <summary>
        ///     Adds a key/value pair to the map if the key does not already exist.
        /// </summary>
        /// <param name="key">
        ///     The key of the element to add.
        /// </param>
        /// <param name="valueFactory">
        ///     The function used to generate a value for the key.
        /// </param>
        /// <returns>
        ///     The value for the key.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            if (key.Equals(this.recentKey))
            {
                return this.recentValue;
            }

            this.recentKey = key;
            return this.recentValue = this.dictionary.GetOrAdd(key, valueFactory);
        }
    }
}
