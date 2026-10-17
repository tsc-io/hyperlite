namespace Hyperlite.Persistence.Collections
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    internal struct IdentityComparer : IEqualityComparer<object>, IEqualityComparer
    {
        /// <summary>
        ///     Determines whether the specified objects are equal.
        /// </summary>
        /// <param name="x">
        ///     The first object of type <see cref="object" /> to compare.
        /// </param>
        /// <param name="y">
        ///     The second object of type <see cref="object" /> to compare.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the specified objects are reference equal, otherwise <c>false</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        /// <summary>
        ///     Returns the runtime hash code for the specified object.
        /// </summary>
        /// <param name="obj">
        ///     The <see cref="object" /> for which a hash code is to be returned.
        /// </param>
        /// <returns>
        ///     A runtime hash code for the specified object.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
