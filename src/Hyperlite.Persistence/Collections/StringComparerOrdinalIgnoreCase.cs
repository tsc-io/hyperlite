namespace Hyperlite.Persistence.Collections
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    internal struct StringComparerOrdinalIgnoreCase : IEqualityComparer<string>
    {
        /// <summary>
        ///     Determines whether the specified strings are equal.
        /// </summary>
        /// <param name="x">
        ///     The first string of type <see cref="string" /> to compare.
        /// </param>
        /// <param name="y">
        ///     The second string of type <see cref="string" /> to compare.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the specified strings are reference equal, otherwise <c>false</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(string x, string y)
        {
            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Returns a hash code for the specified string that ignores case, as
        ///     <see cref="Equals(string, string)" /> does.
        /// </summary>
        /// <param name="obj">
        ///     The <see cref="string" /> for which a hash code is to be returned.
        /// </param>
        /// <returns>
        ///     A hash code equal for every two strings this comparer calls equal.
        /// </returns>
        /// <remarks>
        ///     This used to return <c>obj.GetHashCode()</c>, which is case sensitive. Two
        ///     names differing only in case compared equal but almost always hashed apart, so
        ///     a dictionary on this comparer found a key only when the case matched exactly:
        ///     <c>Inspector</c>'s case-insensitive dedupe of member names mostly did not
        ///     dedupe, and its fallback lookup by name was case sensitive in practice.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(string obj)
        {
            return System.StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
        }
    }
}
