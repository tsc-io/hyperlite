namespace Hyperlite.Persistence
{
    using System;
    using System.Collections.Generic;

    public sealed class ColumnBindingComparer : IEqualityComparer<IColumnBinding>
    {
        /// <summary>
        ///     Determines whether the specified objects are equal.
        /// </summary>
        /// <param name="x">
        ///     The first object of type <see cref="IColumnBinding" /> to compare.
        /// </param>
        /// <param name="y">
        ///     The second object of type <see cref="IColumnBinding" /> to compare.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the specified objects are equal, otherwise <c>false</c>.
        /// </returns>
        public bool Equals(IColumnBinding x, IColumnBinding y)
        {
            if (object.ReferenceEquals(x, y))
            {
                return true;
            }

            if (x == null || y == null)
            {
                return false;
            }

            return string.Equals(x.ColumnFamily, y.ColumnFamily) && string.Equals(x.ColumnQualifier, y.ColumnQualifier);
        }

        /// <summary>
        ///     Returns a hash code for the specified object.
        /// </summary>
        /// <param name="obj">
        ///     The <see cref="IColumnBinding" /> for which a hash code is to be returned.
        /// </param>
        /// <returns>
        ///     A hash code for the specified object.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The type of <paramref name="obj" /> is a reference type and <paramref name="obj" /> is null.
        /// </exception>
        public int GetHashCode(IColumnBinding obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            unchecked
            {
                var hashCode = 17 + obj.ColumnFamily.GetHashCode();
                if (obj.ColumnQualifier != null)
                {
                    hashCode = (29 * hashCode) + obj.ColumnQualifier.GetHashCode();
                }

                return hashCode;
            }
        }
    }
}
