namespace Hyperlite
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// Compares keys by row, column family and column qualifier, optionally including
    /// the timestamp.
    /// </summary>
    /// <remarks>
    /// A null column qualifier compares equal to an empty one here, unlike
    /// <see cref="Key.Equals(Key)" /> which keeps them distinct. That is deliberate: a
    /// cell read back from the database reports an absent qualifier as null, while one
    /// built by a caller may well carry "".
    /// </remarks>
    [Serializable]
    public struct KeyComparer : IEqualityComparer<Key>, IEqualityComparer
    {
        private readonly bool includeTimestamp;

        /// <param name="includeTimestamp">If true the timestamp takes part in the comparison.</param>
        public KeyComparer(bool includeTimestamp)
        {
            this.includeTimestamp = includeTimestamp;
        }

        /// <summary>
        /// Determines whether two keys are equal.
        /// </summary>
        /// <param name="x">The first key to compare, or null.</param>
        /// <param name="y">The second key to compare, or null.</param>
        /// <returns>true if x equals y, otherwise false.</returns>
        public bool Equals(Key x, Key y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (ReferenceEquals(x, null) || ReferenceEquals(y, null))
            {
                return false;
            }

            return string.Equals(x.Row, y.Row)
                   && string.Equals(x.ColumnFamily, y.ColumnFamily)
                   && string.Equals(x.ColumnQualifier ?? string.Empty, y.ColumnQualifier ?? string.Empty)
                   && (!this.includeTimestamp || x.Timestamp == y.Timestamp);
        }

        /// <summary>
        /// Returns the hash code for a key.
        /// </summary>
        /// <param name="obj">Key, mandatory.</param>
        /// <returns>Signed hash code.</returns>
        public int GetHashCode(Key obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var result = 17;
            if (obj.Row != null)
            {
                result = RowInterval.Hash(result, obj.Row.GetHashCode());
            }

            if (obj.ColumnFamily != null)
            {
                result = RowInterval.Hash(result, obj.ColumnFamily.GetHashCode());
            }

            result = RowInterval.Hash(result, (obj.ColumnQualifier ?? string.Empty).GetHashCode());
            if (this.includeTimestamp)
            {
                result = RowInterval.Hash(result, obj.Timestamp.GetHashCode());
            }

            return result;
        }

        /// <summary>
        /// Determines whether two objects, which must both be keys, are equal.
        /// </summary>
        /// <param name="x">The first object to compare, or null.</param>
        /// <param name="y">The second object to compare, or null.</param>
        /// <returns>true if x equals y, otherwise false.</returns>
        public new bool Equals(object x, object y)
        {
            return this.Equals(x as Key, y as Key);
        }

        /// <summary>
        /// Returns the hash code for an object, which must be a key.
        /// </summary>
        /// <param name="obj">Object, mandatory.</param>
        /// <returns>Signed hash code.</returns>
        public int GetHashCode(object obj)
        {
            return this.GetHashCode(obj as Key);
        }
    }
}
