namespace Hyperlite.Persistence.Collections
{
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    internal struct StringComparer : IEqualityComparer<string>
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
            return string.Equals(x, y);
        }

        /// <summary>
        ///     Returns the runtime hash code for the specified string.
        /// </summary>
        /// <param name="obj">
        ///     The <see cref="string" /> for which a hash code is to be returned.
        /// </param>
        /// <returns>
        ///     A runtime hash code for the specified string.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(string obj)
        {
            return obj.GetHashCode();
        }
    }
}
