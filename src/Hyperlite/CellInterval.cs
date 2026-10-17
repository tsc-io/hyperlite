namespace Hyperlite
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Represents a cell interval, a row interval narrowed to a range of columns.
    /// </summary>
    /// <seealso cref="ScanSpec" />
    [Serializable]
    public sealed class CellInterval : RowInterval, IComparable<CellInterval>
    {
        public CellInterval()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CellInterval" /> class with both
        /// bounds inclusive.
        /// </summary>
        /// <param name="startRow">Start row, or null for an open lower bound.</param>
        /// <param name="startColumnFamily">Start column family, mandatory.</param>
        /// <param name="endRow">End row, or null for an open upper bound.</param>
        /// <param name="endColumnFamily">End column family, mandatory.</param>
        public CellInterval(string startRow, string startColumnFamily, string endRow, string endColumnFamily)
            : base(startRow, endRow)
        {
            if (string.IsNullOrEmpty(startColumnFamily))
            {
                throw new ArgumentNullException(nameof(startColumnFamily));
            }

            if (string.IsNullOrEmpty(endColumnFamily))
            {
                throw new ArgumentNullException(nameof(endColumnFamily));
            }

            this.StartColumnFamily = startColumnFamily;
            this.EndColumnFamily = endColumnFamily;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CellInterval" /> class with both
        /// bounds inclusive.
        /// </summary>
        /// <param name="startRow">Start row, or null for an open lower bound.</param>
        /// <param name="startColumnFamily">Start column family, mandatory.</param>
        /// <param name="startColumnQualifier">Start column qualifier, might be null.</param>
        /// <param name="endRow">End row, or null for an open upper bound.</param>
        /// <param name="endColumnFamily">End column family, mandatory.</param>
        /// <param name="endColumnQualifier">End column qualifier, might be null.</param>
        public CellInterval(
            string startRow,
            string startColumnFamily,
            string startColumnQualifier,
            string endRow,
            string endColumnFamily,
            string endColumnQualifier)
            : base(startRow, endRow)
        {
            if (string.IsNullOrEmpty(startColumnFamily))
            {
                throw new ArgumentNullException(nameof(startColumnFamily));
            }

            if (string.IsNullOrEmpty(endColumnFamily))
            {
                throw new ArgumentNullException(nameof(endColumnFamily));
            }

            this.StartColumnFamily = startColumnFamily;
            this.StartColumnQualifier = startColumnQualifier;
            this.EndColumnFamily = endColumnFamily;
            this.EndColumnQualifier = endColumnQualifier;
        }

        /// <param name="startRow">Start row, or null for an open lower bound.</param>
        /// <param name="startColumnFamily">Start column family, mandatory.</param>
        /// <param name="startColumnQualifier">Start column qualifier, might be null.</param>
        /// <param name="includeStartRow">Whether the start cell belongs to the interval.</param>
        /// <param name="endRow">End row, or null for an open upper bound.</param>
        /// <param name="endColumnFamily">End column family, mandatory.</param>
        /// <param name="endColumnQualifier">End column qualifier, might be null.</param>
        /// <param name="includeEndRow">Whether the end cell belongs to the interval.</param>
        public CellInterval(
            string startRow,
            string startColumnFamily,
            string startColumnQualifier,
            bool includeStartRow,
            string endRow,
            string endColumnFamily,
            string endColumnQualifier,
            bool includeEndRow)
            : base(startRow, includeStartRow, endRow, includeEndRow)
        {
            if (string.IsNullOrEmpty(startColumnFamily))
            {
                throw new ArgumentNullException(nameof(startColumnFamily));
            }

            if (string.IsNullOrEmpty(endColumnFamily))
            {
                throw new ArgumentNullException(nameof(endColumnFamily));
            }

            this.StartColumnFamily = startColumnFamily;
            this.StartColumnQualifier = startColumnQualifier;
            this.EndColumnFamily = endColumnFamily;
            this.EndColumnQualifier = endColumnQualifier;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CellInterval" /> class that is a
        /// copy of the specified instance.
        /// </summary>
        /// <param name="cellInterval">Cell interval to copy.</param>
        public CellInterval(CellInterval cellInterval)
            : base(cellInterval)
        {
            // the base constructor already rejected null
            this.StartColumnFamily = cellInterval.StartColumnFamily;
            this.StartColumnQualifier = cellInterval.StartColumnQualifier;
            this.EndColumnFamily = cellInterval.EndColumnFamily;
            this.EndColumnQualifier = cellInterval.EndColumnQualifier;
        }

        public string StartColumnFamily { get; set; }

        public string StartColumnQualifier { get; set; }

        public string EndColumnFamily { get; set; }

        public string EndColumnQualifier { get; set; }

        /// <summary>
        /// Determines whether two specified cell intervals are equal.
        /// </summary>
        /// <param name="x">The first cell interval to compare, or null.</param>
        /// <param name="y">The second cell interval to compare, or null.</param>
        /// <returns>true if x equals y, otherwise false.</returns>
        public static bool operator ==(CellInterval x, CellInterval y)
        {
            return Compare(x, y) == 0;
        }

        /// <summary>
        /// Determines whether two specified cell intervals differ.
        /// </summary>
        /// <param name="x">The first cell interval to compare, or null.</param>
        /// <param name="y">The second cell interval to compare, or null.</param>
        /// <returns>true if x differs from y, otherwise false.</returns>
        public static bool operator !=(CellInterval x, CellInterval y)
        {
            return Compare(x, y) != 0;
        }

        /// <summary>
        /// Determines whether one specified cell interval is less than the other.
        /// </summary>
        /// <param name="x">The first cell interval to compare, or null.</param>
        /// <param name="y">The second cell interval to compare, or null.</param>
        /// <returns>true if x is less than y, otherwise false.</returns>
        public static bool operator <(CellInterval x, CellInterval y)
        {
            return Compare(x, y) < 0;
        }

        /// <summary>
        /// Determines whether one specified cell interval is greater than the other.
        /// </summary>
        /// <param name="x">The first cell interval to compare, or null.</param>
        /// <param name="y">The second cell interval to compare, or null.</param>
        /// <returns>true if x is greater than y, otherwise false.</returns>
        public static bool operator >(CellInterval x, CellInterval y)
        {
            return Compare(x, y) > 0;
        }

        /// <summary>
        /// Compares two specified cell intervals and returns an integer that indicates
        /// their relative position in the sort order.
        /// </summary>
        /// <param name="x">The first cell interval to compare, or null.</param>
        /// <param name="y">The second cell interval to compare, or null.</param>
        /// <returns>A signed integer that indicates the relationship between x and y.</returns>
        public static int Compare(CellInterval x, CellInterval y)
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
        /// Compares this instance with a specified cell interval.
        /// </summary>
        /// <param name="other">Cell interval to compare, or null.</param>
        /// <returns>A signed integer that indicates the relationship to this instance.</returns>
        public int CompareTo(CellInterval other)
        {
            if (ReferenceEquals(other, null))
            {
                return 1;
            }

            if (ReferenceEquals(other, this))
            {
                return 0;
            }

            var result = base.CompareTo(other);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(this.StartColumnFamily, other.StartColumnFamily, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(this.StartColumnQualifier, other.StartColumnQualifier, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(this.EndColumnFamily, other.EndColumnFamily, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            return string.Compare(this.EndColumnQualifier, other.EndColumnQualifier, StringComparison.Ordinal);
        }

        /// <summary>
        /// Compares this instance with a specified row interval. A cell interval compared
        /// against a plain row interval falls back to the row bounds only.
        /// </summary>
        /// <param name="other">Row interval to compare, or null.</param>
        /// <returns>A signed integer that indicates the relationship to this instance.</returns>
        public override int CompareTo(RowInterval other)
        {
            var cellInterval = other as CellInterval;
            return cellInterval != null ? this.CompareTo(cellInterval) : base.CompareTo(other);
        }

        /// <summary>
        /// Determines whether this instance and another cell interval are equal.
        /// </summary>
        /// <param name="other">Cell interval to compare, or null.</param>
        /// <returns>true if other equals this instance, otherwise false.</returns>
        public bool Equals(CellInterval other)
        {
            return this.CompareTo(other) == 0;
        }

        /// <summary>
        /// Determines whether this instance and a specified object, which must also be a
        /// cell interval, are equal.
        /// </summary>
        /// <param name="obj">Object to compare, or null.</param>
        /// <returns>true if obj equals this instance, otherwise false.</returns>
        /// <remarks>
        /// Deliberate deviation: ht4n's override returned the negation of the comparison,
        /// which made Equals(object) report the opposite of Equals(CellInterval) and broke
        /// anything routing through object equality - dictionaries, Contains, Distinct,
        /// Assert.AreEqual. No test covered it. Reproducing the inversion would keep a
        /// latent defect alive, so this returns the correct result.
        /// </remarks>
        public override bool Equals(object obj)
        {
            return this.Equals(obj as CellInterval);
        }

        /// <summary>
        /// Returns the hash code for this cell interval.
        /// </summary>
        /// <returns>Signed hash code.</returns>
        public override int GetHashCode()
        {
            // the row bounds alone, as ht4n had it: the column bounds are part of
            // equality but adding them would change nothing that matters, and a hash
            // that agrees with ht4n's gives the same hash for the same inputs. Note that
            // the inputs are not stable: string.GetHashCode is randomised per process on
            // .NET, so this value may not be persisted or compared across processes
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

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}(StartRow={1}, {2}StartColumnFamily={3}, StartColumnQualifier={4}, EndRow={5}, {6}EndColumnFamily={7}, EndColumnQualifier={8})",
                this.GetType(),
                this.StartRow ?? "null",
                this.IncludeStartRow ? "IncludeStartRow, " : string.Empty,
                this.StartColumnFamily ?? "null",
                this.StartColumnQualifier ?? "null",
                this.EndRow ?? "null",
                this.IncludeEndRow ? "IncludeEndRow, " : string.Empty,
                this.EndColumnFamily ?? "null",
                this.EndColumnQualifier ?? "null");
        }

        /// <summary>
        /// Creates a new object that is a copy of this instance.
        /// </summary>
        /// <returns>A new cell interval equal to this instance.</returns>
        public override object Clone()
        {
            return new CellInterval(this);
        }
    }
}
