namespace Hyperlite
{
    using System;
    using System.Globalization;

    using Hyperlite.Interop;

    /// <summary>
    /// A cell that keeps its value buffer across fills.
    /// </summary>
    /// <remarks>
    /// Handed out by a scanner that has been told to reuse its cell instance. The value
    /// buffer only grows, so a scan over similarly sized values allocates once instead
    /// of once per cell. <see cref="ValueLength" /> is therefore the authoritative
    /// length; the buffer behind <see cref="Value" /> may well be longer.
    /// </remarks>
    [Serializable]
    public sealed class BufferedCell : ICell
    {
        private byte[] value;

        private int valueLength;

        /// <summary>
        /// Initializes a new instance of the <see cref="BufferedCell" /> class with a
        /// pre-allocated value buffer.
        /// </summary>
        /// <param name="capacity">Initial buffer capacity, in bytes.</param>
        public BufferedCell(int capacity)
        {
            this.Flag = CellFlag.Default;
            if (capacity > 0 && capacity < Cell.MaxSize)
            {
                this.value = new byte[capacity];
            }
        }

        /// <summary>
        /// Gets or sets the cell key.
        /// </summary>
        public Key Key { get; set; }

        /// <summary>
        /// Gets the value buffer. Only the first <see cref="ValueLength" /> bytes belong
        /// to the current cell.
        /// </summary>
        public byte[] Value
        {
            get
            {
                return this.value;
            }
        }

        /// <inheritdoc />
        public int ValueLength
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
        /// Copies a native cell in, growing the buffer only when it is too small.
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
                if (this.value == null || this.value.Length < this.valueLength)
                {
                    this.value = new byte[this.valueLength];
                }

                fixed (byte* destination = this.value)
                {
                    Buffer.MemoryCopy(cell.Value, destination, this.value.Length, this.valueLength);
                }
            }

            this.Flag = (CellFlag)cell.Flag;
        }
    }
}
