namespace Hyperlite
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// Compares keys by row alone, so every cell of a row compares equal.
    /// </summary>
    [Serializable]
    public struct RowComparer : IEqualityComparer<Key>, IEqualityComparer
    {
        /// <summary>
        /// Determines whether two keys refer to the same row.
        /// </summary>
        /// <param name="x">The first key to compare, or null.</param>
        /// <param name="y">The second key to compare, or null.</param>
        /// <returns>true if both refer to the same row, otherwise false.</returns>
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

            return string.Equals(x.Row, y.Row);
        }

        /// <summary>
        /// Returns the hash code of a key's row.
        /// </summary>
        /// <param name="obj">Key, mandatory.</param>
        /// <returns>Signed hash code.</returns>
        public int GetHashCode(Key obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            return obj.Row != null ? obj.Row.GetHashCode() : 17;
        }

        /// <summary>
        /// Determines whether two objects, which must both be keys, refer to the same row.
        /// </summary>
        /// <param name="x">The first object to compare, or null.</param>
        /// <param name="y">The second object to compare, or null.</param>
        /// <returns>true if both refer to the same row, otherwise false.</returns>
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
