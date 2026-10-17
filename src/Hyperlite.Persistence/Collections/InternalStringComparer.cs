namespace Hyperlite.Persistence.Collections
{
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
    using System;
    using System.Numerics;
    using System.Runtime.InteropServices;
#endif

    /// <summary>
    ///     Ordinal string equality with a fast, non-randomized hash - for keys the library
    ///     chooses itself, and only for those.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <see cref="string.GetHashCode()" /> is randomized on .NET Core: Marvin, seeded per
    ///     process, which is what stops anyone who controls the keys from choosing a set that
    ///     all collide. It is also slower than the non-randomized hash the framework's own
    ///     dictionaries switch to for a <c>string</c> key with the default comparer, which is
    ///     why a flat table keyed on strings measured slower than <c>ConcurrentDictionary</c>
    ///     on .NET 8 and 10.
    ///     </para>
    ///     <para>
    ///     This is that non-randomized hash, the runtime's own algorithm: two characters at a
    ///     time, reading the string's terminating null for an odd length, as the runtime does.
    ///     On net48 <c>string.GetHashCode</c> is not randomized to begin with, and the
    ///     netstandard2.0 build cannot know which runtime it is on, so both use it unchanged.
    ///     </para>
    ///     <para>
    ///     <b>A non-randomized hash can be flooded.</b> Use this for a table name, a namespace,
    ///     a regular expression the program itself wrote - never for a string that arrives from
    ///     outside, such as a type name read from serialized bytes or a value being serialized.
    ///     Those keep <see cref="StringComparer" /> and the randomized hash.
    ///     </para>
    /// </remarks>
    internal struct InternalStringComparer : IEqualityComparer<string>
    {
        /// <summary>
        ///     Determines whether two strings are ordinally equal.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(string x, string y)
        {
            return string.Equals(x, y);
        }

        /// <summary>
        ///     Returns a non-randomized hash code for the string.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(string obj)
        {
#if NET8_0_OR_GREATER
            return NonRandomizedHash(obj);
#else
            return obj.GetHashCode();
#endif
        }

#if NET8_0_OR_GREATER
        /// <remarks>
        ///     The runtime's <c>string.GetNonRandomizedHashCode</c>, which is internal. A string
        ///     is followed in memory by a null character, so an odd length reads that null as the
        ///     second half of its last pair, exactly as the runtime does. Read through a managed
        ///     reference rather than a pointer: no pinning, and no unsafe context.
        /// </remarks>
        private static int NonRandomizedHash(string value)
        {
            ref var pairs = ref Unsafe.As<char, uint>(ref MemoryMarshal.GetReference(value.AsSpan()));
            var hash1 = (5381u << 16) + 5381u;
            var hash2 = hash1;
            var length = value.Length;
            var index = 0;
            while (length > 2)
            {
                length -= 4;
                hash1 = (BitOperations.RotateLeft(hash1, 5) + hash1) ^ Unsafe.Add(ref pairs, index);
                hash2 = (BitOperations.RotateLeft(hash2, 5) + hash2) ^ Unsafe.Add(ref pairs, index + 1);
                index += 2;
            }

            if (length > 0)
            {
                hash2 = (BitOperations.RotateLeft(hash2, 5) + hash2) ^ Unsafe.Add(ref pairs, index);
            }

            return (int)(hash1 + (hash2 * 1566083941u));
        }
#endif
    }
}
