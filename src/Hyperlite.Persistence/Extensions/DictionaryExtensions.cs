namespace Hyperlite.Persistence.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>
    ///     The few dictionary operations the framework does not provide.
    /// </summary>
    /// <remarks>
    ///     Each of these existed on the hand-written dictionary this library used to carry.
    ///     None needs anything the framework withholds - they are two calls where the
    ///     hand-written one managed with one probe, which is the cost of the swap, and was
    ///     measured.
    /// </remarks>
    internal static class DictionaryExtensions
    {
        /// <summary>
        ///     The value for the key, calling the factory for it if it is not there.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TValue GetOrAdd<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary, TKey key, Func<TKey, TValue> factory)
        {
            TValue existing;
            if (dictionary.TryGetValue(key, out existing))
            {
                return existing;
            }

            existing = factory(key);
            dictionary.Add(key, existing);
            return existing;
        }

        /// <summary>
        ///     Removes the key, handing back what was there.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryRemove<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary, TKey key, out TValue value)
        {
            if (!dictionary.TryGetValue(key, out value))
            {
                return false;
            }

            dictionary.Remove(key);
            return true;
        }
    }
}
