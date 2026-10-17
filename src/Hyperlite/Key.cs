namespace Hyperlite
{
    using System;
    using System.Globalization;
    using System.Text;

    using Hyperlite.Interop;

    /// <summary>
    /// Represents a key, provides accessors to the key attributes.
    /// </summary>
    /// <remarks>
    /// The data model consists of a multi-dimensional table of information
    /// that can be queried using a single primary key. The first dimension of the table
    /// is the row key. The row key is the primary key and defines the order in which the
    /// table data is physically stored. The second dimension is the column family. This
    /// dimension is somewhat analogous to a traditional database column. The third
    /// dimension is the column qualifier. Within each column family, there can be a
    /// theoretically infinite number of qualified instances. The fourth and final
    /// dimension is the time dimension. The key is essentially the concatenation of the
    /// four dimension keys (row, column family, column qualifier and timestamp).
    /// </remarks>
    [Serializable]
    public sealed class Key : IComparable<Key>, IEquatable<Key>, ICloneable
    {
        /// <summary>
        /// The hypertable timestamp epoch.
        /// </summary>
        private static readonly DateTime TimestampOrigin =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public Key()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Key" /> class using the row key.
        /// </summary>
        /// <param name="row">Row key.</param>
        public Key(string row)
        {
            this.Row = row;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Key" /> class using the row key
        /// and the column family.
        /// </summary>
        /// <param name="row">Row key.</param>
        /// <param name="columnFamily">Column family.</param>
        public Key(string row, string columnFamily)
        {
            this.Row = row;
            this.ColumnFamily = columnFamily;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Key" /> class using the row key,
        /// the column family and the column qualifier.
        /// </summary>
        /// <param name="row">Row key.</param>
        /// <param name="columnFamily">Column family.</param>
        /// <param name="columnQualifier">Column qualifier.</param>
        public Key(string row, string columnFamily, string columnQualifier)
        {
            this.Row = row;
            this.ColumnFamily = columnFamily;
            this.ColumnQualifier = columnQualifier;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Key" /> class using the row key,
        /// the column family, the column qualifier and the timestamp.
        /// </summary>
        /// <param name="row">Row key.</param>
        /// <param name="columnFamily">Column family.</param>
        /// <param name="columnQualifier">Column qualifier.</param>
        /// <param name="dateTime">Timestamp.</param>
        public Key(string row, string columnFamily, string columnQualifier, DateTime dateTime)
        {
            this.Row = row;
            this.ColumnFamily = columnFamily;
            this.ColumnQualifier = columnQualifier;
            this.DateTime = dateTime;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Key" /> class that is a copy of
        /// the specified instance.
        /// </summary>
        /// <param name="key">Key to copy.</param>
        public Key(Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Row = key.Row;
            this.ColumnFamily = key.ColumnFamily;
            this.ColumnQualifier = key.ColumnQualifier;
            this.Timestamp = key.Timestamp;
        }

        /// <summary>
        /// Gets or sets the row key.
        /// </summary>
        public string Row { get; set; }

        public string ColumnFamily { get; set; }

        public string ColumnQualifier { get; set; }

        /// <summary>
        /// Gets or sets the full qualified column name, ColumnFamily[:ColumnQualifier].
        /// </summary>
        /// <remarks>
        /// This property combines ColumnFamily and ColumnQualifier.
        /// </remarks>
        public string Column
        {
            get
            {
                if (this.ColumnFamily == null)
                {
                    return null;
                }

                var sb = new StringBuilder();
                sb.Append(this.ColumnFamily);
                if (!string.IsNullOrEmpty(this.ColumnQualifier))
                {
                    sb.Append(":");
                    sb.Append(this.ColumnQualifier);
                }

                return sb.ToString();
            }

            set
            {
                if (value != null)
                {
                    var parts = value.Split(':');
                    this.ColumnFamily = parts[0];
                    this.ColumnQualifier = parts.Length > 1 ? parts[1] : null;
                }
                else
                {
                    this.ColumnFamily = null;
                    this.ColumnQualifier = null;
                }
            }
        }

        /// <summary>
        /// Gets or sets the timestamp in nanoseconds since 1970-01-01 00:00:00.0 UTC.
        /// </summary>
        /// <remarks>
        /// If 0 the database assigns the timestamp automatically on an insert or update
        /// cell operation.
        /// </remarks>
        [CLSCompliant(false)]
        public ulong Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the timestamp.
        /// </summary>
        public DateTime DateTime
        {
            get
            {
                return TimestampOrigin + TimeSpan.FromTicks((long)(this.Timestamp / 100));
            }

            set
            {
                if (value < TimestampOrigin)
                {
                    throw new ArgumentException("Invalid DateTime");
                }

                if (value.Kind == DateTimeKind.Unspecified)
                {
                    throw new ArgumentException("Unspecified DateTime Kind");
                }

                this.Timestamp = (ulong)((value.ToUniversalTime() - TimestampOrigin).Ticks * 100);
            }
        }

        /// <summary>
        /// Determines whether two specified keys are equal.
        /// </summary>
        /// <param name="x">The first key to compare, or null.</param>
        /// <param name="y">The second key to compare, or null.</param>
        /// <returns>true if the value of x is the same as the value of y, otherwise false.</returns>
        public static bool operator ==(Key x, Key y)
        {
            return Compare(x, y) == 0;
        }

        /// <summary>
        /// Determines whether two specified keys are different.
        /// </summary>
        /// <param name="x">The first key to compare, or null.</param>
        /// <param name="y">The second key to compare, or null.</param>
        /// <returns>true if the value of x differs from the value of y, otherwise false.</returns>
        public static bool operator !=(Key x, Key y)
        {
            return Compare(x, y) != 0;
        }

        /// <summary>
        /// Determines whether one specified key is less than the other.
        /// </summary>
        /// <param name="x">The first key to compare, or null.</param>
        /// <param name="y">The second key to compare, or null.</param>
        /// <returns>true if the value of x is less than the value of y, otherwise false.</returns>
        public static bool operator <(Key x, Key y)
        {
            return Compare(x, y) < 0;
        }

        /// <summary>
        /// Determines whether one specified key is greater than the other.
        /// </summary>
        /// <param name="x">The first key to compare, or null.</param>
        /// <param name="y">The second key to compare, or null.</param>
        /// <returns>true if the value of x is greater than the value of y, otherwise false.</returns>
        public static bool operator >(Key x, Key y)
        {
            return Compare(x, y) > 0;
        }

        /// <summary>
        /// Compares two specified keys and returns an integer that indicates their
        /// relative position in the sort order.
        /// </summary>
        /// <param name="x">The first key to compare, or null.</param>
        /// <param name="y">The second key to compare, or null.</param>
        /// <returns>
        /// A signed integer that indicates the relationship between the two comparands:
        /// less than zero if x precedes y, zero if they are equal, greater than zero
        /// if x follows y.
        /// </returns>
        public static int Compare(Key x, Key y)
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
        /// Generates a base85 encoded GUID.
        /// </summary>
        /// <returns>A new base85 encoded GUID.</returns>
        public static string Generate()
        {
            return Base85.Generate();
        }

        /// <summary>
        /// Generates a base85 encoded GUID whose first two characters are taken from the
        /// type name, which gives keys of one kind a common prefix.
        /// </summary>
        /// <param name="type">Type supplying the prefix, or null.</param>
        /// <returns>A new base85 encoded GUID.</returns>
        public static string Generate(Type type)
        {
            if (type == null)
            {
                return Base85.Generate();
            }

            var name = type.Name;
            if (string.IsNullOrEmpty(name))
            {
                return Base85.Generate();
            }

            // first and last character of the type name, as ht4n did
            var prefix = name.Length > 1
                ? new string(new[] { name[0], name[name.Length - 1] })
                : new string(new[] { name[0] });

            return Base85.Generate(prefix);
        }

        /// <summary>
        /// Encodes a specified GUID to base85.
        /// </summary>
        /// <param name="value">GUID to base85 encode.</param>
        /// <returns>base85 encoded GUID.</returns>
        public static string Encode(Guid value)
        {
            return Base85.Encode(value.ToByteArray());
        }

        /// <summary>
        /// Decodes a base85 encoded GUID.
        /// </summary>
        /// <param name="value">base85 GUID to decode.</param>
        /// <returns>Decoded GUID.</returns>
        public static Guid Decode(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Length != Base85.KeySize)
            {
                throw new ArgumentException("Invalid value length", nameof(value));
            }

            return new Guid(Base85.Decode(value));
        }

        /// <summary>
        /// Compares this instance with a specified <see cref="Key" /> and indicates
        /// whether this instance precedes, follows, or appears in the same position in
        /// the sort order.
        /// </summary>
        /// <param name="other">Key to compare, or null.</param>
        /// <returns>
        /// A signed integer that indicates the relationship between the comparand and
        /// this instance.
        /// </returns>
        public int CompareTo(Key other)
        {
            if (ReferenceEquals(other, null))
            {
                return 1;
            }

            if (ReferenceEquals(other, this))
            {
                return 0;
            }

            var result = string.Compare(this.Row, other.Row, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(this.ColumnFamily, other.ColumnFamily, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(this.ColumnQualifier, other.ColumnQualifier, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            return this.Timestamp.CompareTo(other.Timestamp);
        }

        /// <summary>
        /// Determines whether this instance and another <see cref="Key" /> are equal.
        /// </summary>
        /// <param name="other">Key to compare, or null.</param>
        /// <returns>true if the value of other is the same as the value of this instance, otherwise false.</returns>
        public bool Equals(Key other)
        {
            if (ReferenceEquals(other, this))
            {
                return true;
            }

            if (ReferenceEquals(other, null))
            {
                return false;
            }

            return string.Equals(this.Row, other.Row)
                   && string.Equals(this.ColumnFamily, other.ColumnFamily)
                   && string.Equals(this.ColumnQualifier, other.ColumnQualifier)
                   && this.Timestamp == other.Timestamp;
        }

        /// <summary>
        /// Determines whether this instance and a specified object, which must also be a
        /// <see cref="Key" />, are equal.
        /// </summary>
        /// <param name="obj">Key to compare, or null.</param>
        /// <returns>true if the value of obj is the same as the value of this instance, otherwise false.</returns>
        public override bool Equals(object obj)
        {
            return this.Equals(obj as Key);
        }

        /// <summary>
        /// Returns the hash code for this <see cref="Key" />.
        /// </summary>
        /// <returns>Signed hash code.</returns>
        public override int GetHashCode()
        {
            var result = 17;
            if (this.Row != null)
            {
                result = Hash(result, this.Row.GetHashCode());
            }

            if (this.ColumnFamily != null)
            {
                result = Hash(result, this.ColumnFamily.GetHashCode());
            }

            if (this.ColumnQualifier != null)
            {
                result = Hash(result, this.ColumnQualifier.GetHashCode());
            }

            return Hash(result, this.Timestamp.GetHashCode());
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            if (this.Row == null && this.ColumnFamily == null && this.ColumnQualifier == null
                && this.DateTime == TimestampOrigin)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}(Empty)", this.GetType());
            }

            var sb = new StringBuilder();
            sb.Append(this.GetType());
            sb.Append("(");
            Append(sb, "Row", this.Row);
            Append(sb, "ColumnFamily", this.ColumnFamily);
            Append(sb, "ColumnQualifier", this.ColumnQualifier);
            if (this.DateTime != TimestampOrigin)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "DateTime={0}, ", this.DateTime));
            }

            sb.Remove(sb.Length - 2, 2);
            sb.Append(")");
            return sb.ToString();
        }

        /// <summary>
        /// Creates a new object that is a copy of this instance.
        /// </summary>
        /// <returns>A new <see cref="Key" /> equal to this instance.</returns>
        public object Clone()
        {
            return new Key(this);
        }

        /// <summary>
        /// Copies the key attributes out of a native cell.
        /// </summary>
        internal unsafe void From(ref NativeCell cell)
        {
            this.Row = Utf8.ToString(cell.Row, cell.RowLength);

            // a table has a handful of column families, so for an ascii name this hits the
            // cache on every cell after the first and saves a string allocation per cell
            this.ColumnFamily = Utf8.ToStringInterned(cell.ColumnFamily, cell.ColumnFamilyLength);
            this.ColumnQualifier = cell.ColumnQualifier != null
                ? Utf8.ToString(cell.ColumnQualifier, cell.ColumnQualifierLength)
                : null;
            this.Timestamp = cell.Timestamp;
        }

        private static int Hash(int hash, int newHashValue)
        {
            return (hash * 29) + newHashValue;
        }

        private static void Append(StringBuilder sb, string name, string value)
        {
            if (value != null)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}={1}, ", name, value));
            }
        }
    }
}
