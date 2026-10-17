namespace Hyperlite
{
    using System;
    using System.Buffers;
    using System.Globalization;

    using Hyperlite.Interop;

    /// <summary>
    /// A cell whose value buffer is rented from a pool.
    /// </summary>
    /// <remarks>
    /// Handed out by a scanner told to pool its buffers. The caller owns the rented
    /// array until it hands it back with <see cref="Return" />; failing to do so is not
    /// a leak, the pool simply allocates a replacement.
    ///
    /// Two pools, because renting a megabyte to hold a hundred bytes wastes far more
    /// than the extra bucket costs.
    /// </remarks>
    [Serializable]
    public class PooledCell : ICell
    {
        private const int SmallPoolSize = 64 * 1024;

        private const int LargePoolSize = 1024 * 1024;

        private static readonly ArrayPool<byte> SmallPool = ArrayPool<byte>.Create(SmallPoolSize, 1024);

        private static readonly ArrayPool<byte> LargePool = ArrayPool<byte>.Create(LargePoolSize, 32);

        private byte[] value;

        private int valueLength;

        public PooledCell()
        {
            this.Flag = CellFlag.Default;
        }

        /// <summary>
        /// Gets or sets the cell key.
        /// </summary>
        public virtual Key Key { get; set; }

        /// <summary>
        /// Gets the rented value buffer. Only the first <see cref="ValueLength" /> bytes
        /// belong to the current cell.
        /// </summary>
        public virtual byte[] Value
        {
            get
            {
                return this.value;
            }
        }

        /// <inheritdoc />
        public virtual int ValueLength
        {
            get
            {
                return this.valueLength;
            }
        }

        /// <summary>
        /// What writing the cell does: <see cref="CellFlag.Default" /> inserts it, the others delete.
        /// </summary>
        public CellFlag Flag { get; set; }

        /// <summary>
        /// Hands a rented buffer back to the pool it came from.
        /// </summary>
        /// <param name="value">Buffer obtained from <see cref="Value" />, or null.</param>
        public static void Return(byte[] value)
        {
            if (value == null)
            {
                return;
            }

            if (value.Length <= SmallPoolSize)
            {
                SmallPool.Return(value, false);
            }
            else
            {
                LargePool.Return(value, false);
            }
        }

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
                this.ValueLength,
                this.Flag);
        }

        /// <summary>
        /// Copies a native cell in, renting a fresh buffer for the value.
        /// </summary>
        internal unsafe void From(ref NativeCell cell)
        {
            if (this.Key == null)
            {
                this.Key = new Key();
            }

            this.Key.From(ref cell);

            this.valueLength = cell.ValueLength;
            if (this.valueLength > 0 && cell.Value != null)
            {
                this.value = this.valueLength <= SmallPoolSize
                    ? SmallPool.Rent(this.valueLength)
                    : LargePool.Rent(this.valueLength);

                fixed (byte* destination = this.value)
                {
                    Buffer.MemoryCopy(cell.Value, destination, this.value.Length, this.valueLength);
                }
            }
            else
            {
                this.value = null;
            }

            this.Flag = (CellFlag)cell.Flag;
        }
    }
}
