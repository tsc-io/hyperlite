namespace Hyperlite.Interop
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Marshals a batch of cells into one native array for a single crossing.
    /// </summary>
    /// <remarks>
    /// Everything - the cell array, the UTF-8 encoded names and the values - is copied
    /// into one unmanaged block. That is cheaper than pinning every managed string and
    /// array separately, and it means the native side sees a stable, contiguous batch it
    /// can walk in one transaction.
    /// </remarks>
    internal sealed unsafe class CellBatch : IDisposable
    {
        private IntPtr block;

        /// <summary>
        /// Copies the cells into unmanaged memory.
        /// </summary>
        /// <param name="cells">Cells to marshal. Every entry needs a non-null key.</param>
        internal CellBatch(IList<Cell> cells)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            this.Count = cells.Count;

            // One pass to size the block, a second to fill it. The sizing pass bounds each
            // string rather than measuring it: an exact GetByteCount would walk every
            // string a second time, and the encode in the fill pass has to walk it anyway.
            // The block is transient and the strings are short next to the values, so the
            // slack the bound leaves is not worth another pass over the data.
            var cellBytes = sizeof(NativeCell) * (long)cells.Count;
            var payloadBytes = 0L;

            for (var i = 0; i < cells.Count; ++i)
            {
                var key = cells[i].Key;
                payloadBytes += Bound(key.Row);
                payloadBytes += Bound(key.ColumnFamily);
                payloadBytes += key.ColumnQualifier != null ? Bound(key.ColumnQualifier) : 0;
                payloadBytes += cells[i].Value != null ? cells[i].Value.Length : 0;
            }

            this.block = Marshal.AllocHGlobal(new IntPtr(cellBytes + payloadBytes));

            this.Cells = (NativeCell*)this.block;
            var payload = (byte*)this.block + cellBytes;

            for (var i = 0; i < cells.Count; ++i)
            {
                var cell = cells[i];
                var key = cell.Key;

                var native = default(NativeCell);

                native.Row = payload;
                native.RowLength = Write(ref payload, key.Row);

                native.ColumnFamily = payload;
                native.ColumnFamilyLength = Write(ref payload, key.ColumnFamily);

                // a null qualifier stays null, which is what tells the native side the
                // cell has no qualifier as opposed to an empty one
                if (key.ColumnQualifier != null)
                {
                    native.ColumnQualifier = payload;
                    native.ColumnQualifierLength = Write(ref payload, key.ColumnQualifier);
                }
                else
                {
                    native.ColumnQualifier = null;
                    native.ColumnQualifierLength = 0;
                }

                var value = cell.Value;
                if (value != null && value.Length > 0)
                {
                    native.Value = payload;
                    native.ValueLength = value.Length;
                    fixed (byte* source = value)
                    {
                        Buffer.MemoryCopy(source, payload, value.Length, value.Length);
                    }

                    payload += value.Length;
                }
                else
                {
                    native.Value = value != null ? payload : null;
                    native.ValueLength = 0;
                }

                native.Timestamp = key.Timestamp;
                native.Flag = (byte)cell.Flag;

                this.Cells[i] = native;
            }
        }

        /// <summary>
        /// The marshalled cells.
        /// </summary>
        internal NativeCell* Cells { get; private set; }

        /// <summary>
        /// The number of cells.
        /// </summary>
        internal int Count { get; private set; }

        public void Dispose()
        {
            if (this.block != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(this.block);
                this.block = IntPtr.Zero;
                this.Cells = null;
            }
        }

        /// <summary>
        /// Upper bound on the encoded size of <paramref name="value" />, terminator
        /// included. UTF-8 needs at most three bytes per UTF-16 code unit, since a
        /// character outside the basic plane arrives as two units and encodes to four.
        /// </summary>
        private static int Bound(string value)
        {
            return value == null ? 1 : (3 * value.Length) + 1;
        }

        /// <summary>
        /// Encodes a string at the payload cursor and advances it past the terminator.
        /// </summary>
        /// <remarks>
        /// The cursor is guaranteed <see cref="Bound" /> bytes of room by the sizing pass,
        /// so no measuring is needed here either.
        /// </remarks>
        /// <returns>The encoded length, excluding the terminator.</returns>
        private static int Write(ref byte* payload, string value)
        {
            var written = Utf8.Encode(value, payload, Bound(value));
            payload += written + 1;
            return written;
        }
    }
}
