namespace Hyperlite
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Represents a row interval, a range of row keys with inclusive or exclusive bounds.
    /// </summary>
    /// <seealso cref="ScanSpec" />
    [Serializable]
    public class RowInterval : IComparable<RowInterval>, IEquatable<RowInterval>, ICloneable
    {
        public RowInterval()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RowInterval" /> class with both
        /// bounds inclusive.
        /// </summary>
        /// <param name="startRow">Start row, or null for an open lower bound.</param>
        /// <param name="endRow">End row, or null for an open upper bound.</param>
        public RowInterval(string startRow, string endRow)
        {
            this.StartRow = startRow;
            this.IncludeStartRow = true;
            this.EndRow = endRow;
            this.IncludeEndRow = true;
        }

        /// <param name="startRow">Start row, or null for an open lower bound.</param>
        /// <param name="includeStartRow">Whether the start row belongs to the interval.</param>
        /// <param name="endRow">End row, or null for an open upper bound.</param>
        /// <param name="includeEndRow">Whether the end row belongs to the interval.</param>
        public RowInterval(string startRow, bool includeStartRow, string endRow, bool includeEndRow)
        {
            this.StartRow = startRow;
            this.IncludeStartRow = includeStartRow;
            this.EndRow = endRow;
            this.IncludeEndRow = includeEndRow;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RowInterval" /> class that is a
        /// copy of the specified instance.
        /// </summary>
        /// <param name="rowInterval">Row interval to copy.</param>
        public RowInterval(RowInterval rowInterval)
        {
            if (rowInterval == null)
            {
                throw new ArgumentNullException(nameof(rowInterval));
            }

            this.StartRow = rowInterval.StartRow;
            this.IncludeStartRow = rowInterval.IncludeStartRow;
            this.EndRow = rowInterval.EndRow;
            this.IncludeEndRow = rowInterval.IncludeEndRow;
        }

        public string StartRow { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the start row belongs to the interval.
        /// </summary>
        public bool IncludeStartRow { get; set; }

        public string EndRow { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the end row belongs to the interval.
        /// </summary>
        public bool IncludeEndRow { get; set; }

        /// <summary>
        /// Determines whether two specified row intervals are equal.
        /// </summary>
        /// <param name="x">The first row interval to compare, or null.</param>
        /// <param name="y">The second row interval to compare, or null.</param>
        /// <returns>true if x equals y, otherwise false.</returns>
        public static bool operator ==(RowInterval x, RowInterval y)
        {
            return Compare(x, y) == 0;
        }

        /// <summary>
        /// Determines whether two specified row intervals differ.
        /// </summary>
        /// <param name="x">The first row interval to compare, or null.</param>
        /// <param name="y">The second row interval to compare, or null.</param>
        /// <returns>true if x differs from y, otherwise false.</returns>
        public static bool operator !=(RowInterval x, RowInterval y)
        {
            return Compare(x, y) != 0;
        }

        /// <summary>
        /// Determines whether one specified row interval is less than the other.
        /// </summary>
        /// <param name="x">The first row interval to compare, or null.</param>
        /// <param name="y">The second row interval to compare, or null.</param>
        /// <returns>true if x is less than y, otherwise false.</returns>
        public static bool operator <(RowInterval x, RowInterval y)
        {
            return Compare(x, y) < 0;
        }

        /// <summary>
        /// Determines whether one specified row interval is greater than the other.
        /// </summary>
        /// <param name="x">The first row interval to compare, or null.</param>
        /// <param name="y">The second row interval to compare, or null.</param>
        /// <returns>true if x is greater than y, otherwise false.</returns>
        public static bool operator >(RowInterval x, RowInterval y)
        {
            return Compare(x, y) > 0;
        }

        /// <summary>
        /// Compares two specified row intervals and returns an integer that indicates
        /// their relative position in the sort order.
        /// </summary>
        /// <param name="x">The first row interval to compare, or null.</param>
        /// <param name="y">The second row interval to compare, or null.</param>
        /// <returns>A signed integer that indicates the relationship between x and y.</returns>
        public static int Compare(RowInterval x, RowInterval y)
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
        /// Compares this instance with a specified row interval.
        /// </summary>
        /// <param name="other">Row interval to compare, or null.</param>
        /// <returns>A signed integer that indicates the relationship to this instance.</returns>
        public virtual int CompareTo(RowInterval other)
        {
            if (ReferenceEquals(other, null))
            {
                return 1;
            }

            if (ReferenceEquals(other, this))
            {
                return 0;
            }

            var result = string.Compare(this.StartRow, other.StartRow, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = this.IncludeStartRow.CompareTo(other.IncludeStartRow);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(this.EndRow, other.EndRow, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            return this.IncludeEndRow.CompareTo(other.IncludeEndRow);
        }

        /// <summary>
        /// Determines whether this instance and another row interval are equal.
        /// </summary>
        /// <param name="other">Row interval to compare, or null.</param>
        /// <returns>true if other equals this instance, otherwise false.</returns>
        public virtual bool Equals(RowInterval other)
        {
            return this.CompareTo(other) == 0;
        }

        /// <summary>
        /// Determines whether this instance and a specified object, which must also be a
        /// row interval, are equal.
        /// </summary>
        /// <param name="obj">Object to compare, or null.</param>
        /// <returns>true if obj equals this instance, otherwise false.</returns>
        public override bool Equals(object obj)
        {
            return this.Equals(obj as RowInterval);
        }

        /// <summary>
        /// Returns the hash code for this row interval.
        /// </summary>
        /// <returns>Signed hash code.</returns>
        public override int GetHashCode()
        {
            var result = 17;
            if (this.StartRow != null)
            {
                result = Hash(result, this.StartRow.GetHashCode());
            }

            result = Hash(result, this.IncludeStartRow.GetHashCode());
            if (this.EndRow != null)
            {
                result = Hash(result, this.EndRow.GetHashCode());
            }

            return Hash(result, this.IncludeEndRow.GetHashCode());
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}(StartRow={1}, {2}EndRow={3}{4})",
                this.GetType(),
                this.StartRow ?? "null",
                this.IncludeStartRow ? "IncludeStartRow, " : string.Empty,
                this.EndRow ?? "null",
                this.IncludeEndRow ? ", IncludeEndRow" : string.Empty);
        }

        /// <summary>
        /// Creates a new object that is a copy of this instance.
        /// </summary>
        /// <returns>A new row interval equal to this instance.</returns>
        public virtual object Clone()
        {
            return new RowInterval(this);
        }

        /// <summary>
        /// The hash combiner ht4n used, kept so that a hash computed here matches one
        /// computed there for the same inputs.
        /// </summary>
        /// <remarks>
        /// That is a narrower guarantee than it looks. Every caller folds in
        /// string.GetHashCode, which .NET randomises per process - the same
        /// RowInterval("r1", "r2") hashes differently in two runs of the same build - so
        /// these values must never be persisted or compared across processes. Only the
        /// combiner is stable, and only for inputs that are themselves stable.
        /// </remarks>
        internal static int Hash(int hash, int newHashValue)
        {
            return (hash * 29) + newHashValue;
        }
    }
}
