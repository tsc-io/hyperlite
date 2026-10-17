namespace Hyperlite
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Represents a column predicate, a filter on a column family's qualifier or value.
    /// </summary>
    /// <seealso cref="ScanSpec" />
    /// <seealso cref="MatchKind" />
    [Serializable]
    public sealed class ColumnPredicate : IComparable<ColumnPredicate>, IEquatable<ColumnPredicate>, ICloneable
    {
        public ColumnPredicate()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ColumnPredicate" /> class matching
        /// on the cell value.
        /// </summary>
        /// <param name="columnFamily">Column family, mandatory.</param>
        /// <param name="match">Match kind.</param>
        /// <param name="searchValue">Value to match against, might be null.</param>
        public ColumnPredicate(string columnFamily, MatchKind match, byte[] searchValue)
        {
            if (string.IsNullOrEmpty(columnFamily))
            {
                throw new ArgumentNullException(nameof(columnFamily));
            }

            if (match == MatchKind.Undefined)
            {
                throw new ArgumentException("Invalid match", nameof(match));
            }

            this.ColumnFamily = columnFamily;
            this.Match = match;
            this.SearchValue = searchValue;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ColumnPredicate" /> class matching
        /// on the column qualifier.
        /// </summary>
        /// <param name="columnFamily">Column family, mandatory.</param>
        /// <param name="columnQualifier">Column qualifier to match against, might be null.</param>
        /// <param name="match">Match kind, has to be one of the qualifier kinds.</param>
        public ColumnPredicate(string columnFamily, string columnQualifier, MatchKind match)
        {
            if (string.IsNullOrEmpty(columnFamily))
            {
                throw new ArgumentNullException(nameof(columnFamily));
            }

            if (match != MatchKind.QualifierExact
                && match != MatchKind.QualifierPrefix
                && match != MatchKind.QualifierRegex)
            {
                throw new ArgumentException("Invalid match", nameof(match));
            }

            this.ColumnFamily = columnFamily;
            this.ColumnQualifier = columnQualifier;
            this.Match = match;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ColumnPredicate" /> class matching
        /// on the column qualifier and the cell value.
        /// </summary>
        /// <param name="columnFamily">Column family, mandatory.</param>
        /// <param name="columnQualifier">Column qualifier to match against, might be null.</param>
        /// <param name="match">Match kind.</param>
        /// <param name="searchValue">Value to match against, might be null.</param>
        public ColumnPredicate(string columnFamily, string columnQualifier, MatchKind match, byte[] searchValue)
        {
            if (string.IsNullOrEmpty(columnFamily))
            {
                throw new ArgumentNullException(nameof(columnFamily));
            }

            if (match == MatchKind.Undefined)
            {
                throw new ArgumentException("Invalid match", nameof(match));
            }

            this.ColumnFamily = columnFamily;
            this.ColumnQualifier = columnQualifier;
            this.Match = match;
            this.SearchValue = searchValue;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ColumnPredicate" /> class that is
        /// a copy of the specified instance.
        /// </summary>
        /// <param name="columnPredicate">Column predicate to copy.</param>
        public ColumnPredicate(ColumnPredicate columnPredicate)
        {
            if (columnPredicate == null)
            {
                throw new ArgumentNullException(nameof(columnPredicate));
            }

            this.ColumnFamily = columnPredicate.ColumnFamily;
            this.ColumnQualifier = columnPredicate.ColumnQualifier;
            this.Match = columnPredicate.Match;
            this.SearchValue = columnPredicate.SearchValue;
        }

        public string ColumnFamily { get; set; }

        public string ColumnQualifier { get; set; }

        /// <summary>
        /// What the predicate compares - the value, the qualifier or both - and how.
        /// </summary>
        public MatchKind Match { get; set; }

        /// <summary>
        /// Gets or sets the value to match against.
        /// </summary>
        public byte[] SearchValue { get; set; }

        /// <summary>
        /// Determines whether two specified column predicates are equal.
        /// </summary>
        /// <param name="x">The first column predicate to compare, or null.</param>
        /// <param name="y">The second column predicate to compare, or null.</param>
        /// <returns>true if x equals y, otherwise false.</returns>
        public static bool operator ==(ColumnPredicate x, ColumnPredicate y)
        {
            return Compare(x, y) == 0;
        }

        /// <summary>
        /// Determines whether two specified column predicates differ.
        /// </summary>
        /// <param name="x">The first column predicate to compare, or null.</param>
        /// <param name="y">The second column predicate to compare, or null.</param>
        /// <returns>true if x differs from y, otherwise false.</returns>
        public static bool operator !=(ColumnPredicate x, ColumnPredicate y)
        {
            return Compare(x, y) != 0;
        }

        /// <summary>
        /// Determines whether one specified column predicate is less than the other.
        /// </summary>
        /// <param name="x">The first column predicate to compare, or null.</param>
        /// <param name="y">The second column predicate to compare, or null.</param>
        /// <returns>true if x is less than y, otherwise false.</returns>
        public static bool operator <(ColumnPredicate x, ColumnPredicate y)
        {
            return Compare(x, y) < 0;
        }

        /// <summary>
        /// Determines whether one specified column predicate is greater than the other.
        /// </summary>
        /// <param name="x">The first column predicate to compare, or null.</param>
        /// <param name="y">The second column predicate to compare, or null.</param>
        /// <returns>true if x is greater than y, otherwise false.</returns>
        public static bool operator >(ColumnPredicate x, ColumnPredicate y)
        {
            return Compare(x, y) > 0;
        }

        /// <summary>
        /// Compares two specified column predicates and returns an integer that indicates
        /// their relative position in the sort order.
        /// </summary>
        /// <param name="x">The first column predicate to compare, or null.</param>
        /// <param name="y">The second column predicate to compare, or null.</param>
        /// <returns>A signed integer that indicates the relationship between x and y.</returns>
        public static int Compare(ColumnPredicate x, ColumnPredicate y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (ReferenceEquals(y, null))
            {
                return 1;
            }

            if (ReferenceEquals(x, null))
            {
                return -1;
            }

            return x.CompareTo(y);
        }

        /// <summary>
        /// Compares this instance with a specified column predicate.
        /// </summary>
        /// <param name="other">Column predicate to compare, or null.</param>
        /// <returns>A signed integer that indicates the relationship to this instance.</returns>
        public int CompareTo(ColumnPredicate other)
        {
            if (ReferenceEquals(other, null))
            {
                return 1;
            }

            if (ReferenceEquals(other, this))
            {
                return 0;
            }

            var result = string.Compare(this.ColumnFamily, other.ColumnFamily, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(this.ColumnQualifier, other.ColumnQualifier, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = this.Match.CompareTo(other.Match);
            if (result != 0)
            {
                return result;
            }

            return CompareArray(this.SearchValue, other.SearchValue);
        }

        /// <summary>
        /// Determines whether this instance and another column predicate are equal.
        /// </summary>
        /// <param name="other">Column predicate to compare, or null.</param>
        /// <returns>true if other equals this instance, otherwise false.</returns>
        public bool Equals(ColumnPredicate other)
        {
            return this.CompareTo(other) == 0;
        }

        /// <summary>
        /// Determines whether this instance and a specified object, which must also be a
        /// column predicate, are equal.
        /// </summary>
        /// <param name="obj">Object to compare, or null.</param>
        /// <returns>true if obj equals this instance, otherwise false.</returns>
        public override bool Equals(object obj)
        {
            return this.Equals(obj as ColumnPredicate);
        }

        /// <summary>
        /// Returns the hash code for this column predicate.
        /// </summary>
        /// <returns>Signed hash code.</returns>
        public override int GetHashCode()
        {
            var result = 17;
            if (this.ColumnFamily != null)
            {
                result = RowInterval.Hash(result, this.ColumnFamily.GetHashCode());
            }

            if (this.ColumnQualifier != null)
            {
                result = RowInterval.Hash(result, this.ColumnQualifier.GetHashCode());
            }

            result = RowInterval.Hash(result, this.Match.GetHashCode());
            if (this.SearchValue != null)
            {
                result = RowInterval.Hash(result, HashArray(this.SearchValue));
            }

            return result;
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}(ColumnFamily={1}, ColumnQualifier={2}, Match={3})",
                this.GetType(),
                this.ColumnFamily ?? "null",
                this.ColumnQualifier ?? "null",
                this.Match);
        }

        /// <summary>
        /// Creates a new object that is a copy of this instance.
        /// </summary>
        /// <returns>A new column predicate equal to this instance.</returns>
        public object Clone()
        {
            return new ColumnPredicate(this);
        }

        /// <summary>
        /// Orders by length first, then bytewise, which is what ht4n did.
        /// </summary>
        private static int CompareArray(byte[] x, byte[] y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (ReferenceEquals(y, null))
            {
                return 1;
            }

            if (ReferenceEquals(x, null))
            {
                return -1;
            }

            var lengthDifference = x.Length - y.Length;
            if (lengthDifference != 0)
            {
                return Math.Sign(lengthDifference);
            }

            for (var i = 0; i < x.Length; ++i)
            {
                if (x[i] != y[i])
                {
                    return x[i] < y[i] ? -1 : 1;
                }
            }

            return 0;
        }

        /// <summary>
        /// Byte array hash, computed the way ht4n computed it.
        /// </summary>
        private static int HashArray(byte[] x)
        {
            if (x == null)
            {
                return 0;
            }

            if (x.Length == 0)
            {
                return 31;
            }

            // ht4n's loop was `while (--len) hash ^= 617 * (*p++)`, which visits len - 1
            // bytes and skips the last one. So a one byte array hashes the same whatever
            // the byte is, and the last byte of any array never reaches the hash.
            //
            // Kept as is, because a hash only has to be consistent and equality does
            // compare every byte. It reads like a defect, so TestValueSemantics pins it -
            // the next person to notice should find a test saying it is deliberate rather
            // than a bug to fix.
            var hash = 0;
            for (var i = 0; i < x.Length - 1; ++i)
            {
                hash ^= 617 * x[i];
            }

            return hash;
        }
    }
}
