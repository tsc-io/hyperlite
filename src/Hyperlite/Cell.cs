namespace Hyperlite
{
    using System;
    using System.Globalization;

    using Hyperlite.Interop;

    /// <summary>
    /// A key and its value, the unit a table stores and a scanner returns.
    /// </summary>
    /// <example>
    /// <code>
    /// var cell = new Cell();
    /// cell.Key = new Key { Row = "a", ColumnFamily = "cf" };
    /// cell.Value = Encoding.UTF8.GetBytes("some value");
    /// </code>
    /// </example>
    public sealed class Cell : ICell
    {
        public Cell()
        {
            this.Flag = CellFlag.Default;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cell" /> class using the key.
        /// </summary>
        /// <param name="key">Cell key.</param>
        public Cell(Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Key = key;
            this.Flag = CellFlag.Default;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cell" /> class using the key and
        /// the value.
        /// </summary>
        /// <param name="key">Cell key.</param>
        /// <param name="value">Cell value, might be null.</param>
        public Cell(Key key, byte[] value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Key = key;
            this.Value = value;
            this.Flag = CellFlag.Default;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cell" /> class using the key and
        /// the value.
        /// </summary>
        /// <param name="key">Cell key.</param>
        /// <param name="value">Cell value, might be null.</param>
        /// <param name="cloneKey">If true the key gets cloned, so a caller can keep
        /// reusing the instance it passed in.</param>
        public Cell(Key key, byte[] value, bool cloneKey)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Key = cloneKey ? (Key)key.Clone() : key;
            this.Value = value;
            this.Flag = CellFlag.Default;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cell" /> class using the key and
        /// the cell flag.
        /// </summary>
        /// <param name="key">Cell key.</param>
        /// <param name="flag">Cell flag.</param>
        public Cell(Key key, CellFlag flag)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Key = key;
            this.Flag = flag;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cell" /> class using the key and
        /// the cell flag.
        /// </summary>
        /// <param name="key">Cell key.</param>
        /// <param name="flag">Cell flag.</param>
        /// <param name="cloneKey">If true the key gets cloned.</param>
        public Cell(Key key, CellFlag flag, bool cloneKey)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Key = cloneKey ? (Key)key.Clone() : key;
            this.Flag = flag;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cell" /> class using the key, the
        /// value and the cell flag.
        /// </summary>
        /// <param name="key">Cell key.</param>
        /// <param name="value">Cell value, might be null.</param>
        /// <param name="flag">Cell flag.</param>
        public Cell(Key key, byte[] value, CellFlag flag)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Key = key;
            this.Value = value;
            this.Flag = flag;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cell" /> class using the key, the
        /// value and the cell flag.
        /// </summary>
        /// <param name="key">Cell key.</param>
        /// <param name="value">Cell value, might be null.</param>
        /// <param name="flag">Cell flag.</param>
        /// <param name="cloneKey">If true the key gets cloned.</param>
        public Cell(Key key, byte[] value, CellFlag flag, bool cloneKey)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Key = cloneKey ? (Key)key.Clone() : key;
            this.Value = value;
            this.Flag = flag;
        }

        /// <summary>
        /// Gets the maximum cell size.
        /// </summary>
        public static int MaxSize
        {
            get
            {
                return NativeLimits.MaxCellSize;
            }
        }

        /// <summary>
        /// Gets or sets the cell key.
        /// </summary>
        public Key Key { get; set; }

        /// <summary>
        /// Gets or sets the cell value, might be null.
        /// </summary>
        public byte[] Value { get; set; }

        /// <inheritdoc />
        public int ValueLength
        {
            get
            {
                return this.Value != null ? this.Value.Length : 0;
            }
        }

        /// <summary>
        /// What writing the cell does: <see cref="CellFlag.Default" /> inserts it, the others delete.
        /// </summary>
        public CellFlag Flag { get; set; }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}(Key={1}, Value.Length={2}, Flag={3})",
                this.GetType(),
                this.Key != null ? this.Key.ToString() : "null",
                this.Value != null ? this.Value.Length : 0,
                this.Flag);
        }

        /// <summary>
        /// The delete flag implied by what the key actually specifies.
        /// </summary>
        internal static CellFlag DeleteFlagFromKey(Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            return string.IsNullOrEmpty(key.ColumnFamily)
                ? CellFlag.DeleteRow
                : key.ColumnQualifier != null
                    ? CellFlag.DeleteCell
                    : CellFlag.DeleteColumnFamily;
        }

        /// <summary>
        /// Copies a native cell into this instance, reusing the value buffer where it
        /// already has the right size.
        /// </summary>
        internal unsafe void From(ref NativeCell cell)
        {
            if (this.Key == null)
            {
                this.Key = new Key();
            }

            this.Key.From(ref cell);

            var length = cell.ValueLength;
            if (length > 0 && cell.Value != null)
            {
                if (this.Value == null || this.Value.Length != length)
                {
                    this.Value = new byte[length];
                }

                fixed (byte* destination = this.Value)
                {
                    Buffer.MemoryCopy(cell.Value, destination, length, length);
                }
            }
            else
            {
                this.Value = null;
            }

            this.Flag = (CellFlag)cell.Flag;
        }
    }
}
