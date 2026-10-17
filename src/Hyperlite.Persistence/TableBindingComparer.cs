namespace Hyperlite.Persistence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     Compares two table bindings.
    /// </summary>
    internal sealed class TableBindingComparer : IEqualityComparer<ITableBinding>
    {
        /// <summary>
        ///     Determines whether the specified objects are equal.
        /// </summary>
        /// <param name="x">
        ///     The first object of type <see cref="ITableBinding" /> to compare.
        /// </param>
        /// <param name="y">
        ///     The second object of type <see cref="ITableBinding" /> to compare.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the specified objects are equal, otherwise <c>false</c>.
        /// </returns>
        public bool Equals(ITableBinding x, ITableBinding y)
        {
            if (x == null)
            {
                throw new ArgumentNullException(nameof(x));
            }

            if (y == null)
            {
                throw new ArgumentNullException(nameof(y));
            }

            return string.Equals(x.Namespace, y.Namespace) && string.Equals(x.TableName, y.TableName);
        }

        /// <summary>
        ///     Returns a hash code for the specified object.
        /// </summary>
        /// <param name="tableBinding">
        ///     The <see cref="ITableBinding" /> for which a hash code is to be returned.
        /// </param>
        /// <returns>
        ///     A hash code for the specified object.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The type of <paramref name="tableBinding" /> is a reference type and <paramref name="tableBinding" /> is null.
        /// </exception>
        public int GetHashCode(ITableBinding tableBinding)
        {
            if (tableBinding == null)
            {
                throw new ArgumentNullException(nameof(tableBinding));
            }

            unchecked
            {
                var hashCode = 17 + tableBinding.TableName.GetHashCode();
                if (tableBinding.Namespace != null)
                {
                    hashCode = (29 * hashCode) + tableBinding.Namespace.GetHashCode();
                }

                return hashCode;
            }
        }
    }
}
