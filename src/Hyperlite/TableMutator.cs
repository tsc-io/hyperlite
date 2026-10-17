namespace Hyperlite
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.ExceptionServices;

    using Hyperlite.Interop;

    /// <summary>
    /// The regular table mutator: every operation goes straight to the provider.
    /// </summary>
    /// <remarks>
    /// Safe to share between threads. Every entry point takes the mutator's lock around
    /// its native call, which is also what makes disposing it safe against a call in
    /// flight - that call finds the handle released and throws ObjectDisposedException.
    /// </remarks>
    public class TableMutator : ITableMutator
    {
        private readonly object syncRoot = new object();

        private IntPtr handle;

        private bool disposed;

        internal TableMutator(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            this.handle = handle;
        }

        /// <inheritdoc />
        public bool IsDisposed
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.disposed;
                }
            }
        }

        /// <inheritdoc />
        public void Set(Key key, byte[] value)
        {
            this.Set(key, value, false);
        }

        /// <inheritdoc />
        public void Set(Key key, byte[] value, bool createRowKey)
        {
            this.Set(key, value, CellFlag.Default, createRowKey);
        }

        /// <inheritdoc />
        public unsafe void Set(Key key, IntPtr value, int valueLength)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (valueLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(valueLength));
            }

            if (value == IntPtr.Zero && valueLength > 0)
            {
                throw new ArgumentNullException(nameof(value));
            }

            this.Set(key, valueLength != 0 ? (byte*)value : null, valueLength);
        }

        /// <inheritdoc />
        public void Set(Cell cell)
        {
            if (cell == null)
            {
                throw new ArgumentNullException(nameof(cell));
            }

            this.Set(cell, false);
        }

        /// <inheritdoc />
        public void Set(Cell cell, bool createRowKey)
        {
            if (cell == null)
            {
                throw new ArgumentNullException(nameof(cell));
            }

            this.Set(cell.Key, cell.Value, cell.Flag, createRowKey);
        }

        /// <inheritdoc />
        public void Set(IEnumerable<Cell> cells)
        {
            this.Set(cells, false);
        }

        /// <inheritdoc />
        public virtual unsafe void Set(IEnumerable<Cell> cells, bool createRowKey)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            // Batched: the whole set goes over in one call, inside one ambient
            // transaction. The row keys are generated up front and written back to the
            // caller's cells, as ht4n did, so the caller can read them afterwards.
            var batch = CheckedBatch(cells);

            // a row key is generated for a cell written, never for a delete: one given a
            // generated row would delete nothing, and say nothing
            foreach (var cell in batch)
            {
                if (cell.Flag == CellFlag.Default && (createRowKey || string.IsNullOrEmpty(cell.Key.Row)))
                {
                    cell.Key.Row = Base85.Generate();
                }
            }

            if (batch.Count == 0)
            {
                return;
            }

            using (var pinned = new CellBatch(batch))
            {
                lock (this.syncRoot)
                {
                    var current = this.Handle;
                    Native.Check(Native.Table.MutSetMany(current, pinned.Cells, batch.Count));
                }
            }
        }

        /// <inheritdoc />
        public virtual void Delete(string row)
        {
            if (string.IsNullOrEmpty(row))
            {
                throw new ArgumentException("Invalid parameter row (null or empty)", nameof(row));
            }

            this.Remove(row, null, null, 0);
        }

        /// <inheritdoc />
        public virtual void Delete(Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Remove(key.Row, key.ColumnFamily, key.ColumnQualifier, key.Timestamp);
        }

        /// <inheritdoc />
        public virtual void Delete(IEnumerable<Key> keys)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            foreach (var key in keys)
            {
                if (key != null)
                {
                    this.Remove(key.Row, key.ColumnFamily, key.ColumnQualifier, key.Timestamp);
                }
            }
        }

        /// <inheritdoc />
        public virtual void Delete(IEnumerable<Cell> cells)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            foreach (var cell in cells)
            {
                if (cell != null && cell.Key != null)
                {
                    var key = cell.Key;
                    this.Remove(key.Row, key.ColumnFamily, key.ColumnQualifier, key.Timestamp);
                }
            }
        }

        /// <inheritdoc />
        public virtual unsafe void Flush()
        {
            lock (this.syncRoot)
            {
                var current = this.Handle;
                Native.Check(Native.Table.MutFlush(current));
            }
        }

        /// <summary>
        /// Flushes and releases this mutator.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Flushes and releases this mutator.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()" />.</param>
        protected virtual unsafe void Dispose(bool disposing)
        {
            IntPtr released;
            lock (this.syncRoot)
            {
                if (this.disposed)
                {
                    return;
                }

                this.disposed = true;
                released = this.handle;
                this.handle = IntPtr.Zero;
            }

            if (released != IntPtr.Zero)
            {
                // flushed here rather than left to the release: the release has to succeed,
                // so it cannot report a failed flush, and the writes would be lost without a
                // word. A failure still releases the mutator, then throws - the flush's
                // failure rather than the release's, which would only follow from it.
                ExceptionDispatchInfo flushFailure = null;
                if (disposing)
                {
                    try
                    {
                        Native.Check(Native.Table.MutFlush(released));
                    }
                    catch (Exception e)
                    {
                        flushFailure = ExceptionDispatchInfo.Capture(e);
                    }
                }

                var status = Native.Table.MutRelease(released);
                flushFailure?.Throw();
                Native.Check(status);
            }
        }

        /// <summary>
        /// The cells of a batch, checked as a whole before any of them is taken - by this
        /// mutator, the chunked and the queued one alike - so that a refused batch leaves the
        /// mutator, and the caller's cells, as they were.
        /// </summary>
        internal static List<Cell> CheckedBatch(IEnumerable<Cell> cells)
        {
            var batch = new List<Cell>();
            foreach (var cell in cells)
            {
                if (cell == null || cell.Key == null)
                {
                    continue;
                }

                if (cell.Value != null && cell.Value.Length > NativeLimits.MaxCellSize)
                {
                    // the same guard ht4c::Common::Cells::add applied, and the same
                    // exception ht4n surfaced for it
                    throw new ArgumentException("cell value exceeds the limit", "valueLength");
                }

                if (cell.Flag != CellFlag.Default && string.IsNullOrEmpty(cell.Key.Row))
                {
                    throw new BadKeyException("Invalid row key - empty");
                }

                batch.Add(cell);
            }

            return batch;
        }

        /// <summary>
        /// Writes one cell. The single point every non-batched Set funnels through.
        /// </summary>
        protected virtual unsafe void Set(Key key, byte[] value, CellFlag cellFlag, bool createRowKey)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            fixed (byte* pinned = value)
            {
                this.Write(
                    key,
                    value != null && value.Length > 0 ? pinned : null,
                    value != null ? value.Length : 0,
                    cellFlag,
                    createRowKey);
            }
        }

        /// <summary>
        /// Writes a value the caller owns and keeps. The native mutator copies it, so it is
        /// written straight through; a mutator that holds cells back copies it instead.
        /// </summary>
        internal virtual unsafe void Set(Key key, byte* value, int valueLength)
        {
            this.Write(key, value, valueLength, CellFlag.Default, false);
        }

        /// <summary>
        /// The native handle. Only valid while the lock is held.
        /// </summary>
        internal IntPtr Handle
        {
            get
            {
                if (this.disposed || this.handle == IntPtr.Zero)
                {
                    throw new ObjectDisposedException(nameof(TableMutator));
                }

                return this.handle;
            }
        }

        /// <summary>
        /// Stack space for the three strings of one cell.
        /// </summary>
        /// <remarks>
        /// A row key, a column family and a qualifier are all short - a generated key is
        /// 20 characters and a family is one or two - so one block covers all three and
        /// the per cell write path allocates nothing at all. Anything longer spills to the
        /// heap, which stays correct but is not the case worth tuning for.
        /// </remarks>
        private const int StackLimit = 384;

        /// <summary>
        /// Upper bound on the encoded size of <paramref name="value" />, terminator
        /// included, without walking the string.
        /// </summary>
        /// <remarks>
        /// UTF-8 never needs more than three bytes per UTF-16 code unit: a character
        /// outside the basic plane arrives as two units and encodes to four bytes, so two
        /// bytes per unit. GetByteCount would give the exact figure but costs a second
        /// pass over the string, and a bound is all that is needed to place it.
        /// </remarks>
        private static int Bound(string value)
        {
            return value == null ? 0 : (3 * value.Length) + 1;
        }

        /// <summary>
        /// Encodes one string into the block at <paramref name="at" />, advancing it.
        /// </summary>
        /// <returns>
        /// Pointer to the encoded bytes, or null where <paramref name="value" /> is null:
        /// the abi distinguishes a null string from an empty one.
        /// </returns>
        private static unsafe byte* Place(
            string value, byte* block, int capacity, ref int at, out int length)
        {
            if (value == null)
            {
                length = 0;
                return null;
            }

            var destination = block + at;
            length = Utf8.Encode(value, destination, capacity - at);
            at += length + 1;
            return destination;
        }

        private unsafe void Write(Key key, byte* value, int valueLength, CellFlag cellFlag, bool createRowKey)
        {
            if (valueLength > NativeLimits.MaxCellSize)
            {
                // as for the batches, see Set(IEnumerable<Cell>, bool)
                throw new ArgumentException("cell value exceeds the limit", "valueLength");
            }

            var generate = cellFlag == CellFlag.Default && (createRowKey || string.IsNullOrEmpty(key.Row));
            var row = generate ? null : key.Row;
            var family = key.ColumnFamily;
            var qualifier = key.ColumnQualifier;

            var bound = Bound(row) + Bound(family) + Bound(qualifier);
            var spill = bound > StackLimit ? new byte[bound] : null;

            var stack = stackalloc byte[StackLimit];
            fixed (byte* pinned = spill)
            {
                var block = spill != null ? pinned : stack;
                var capacity = spill != null ? bound : StackLimit;
                var at = 0;

                var cell = default(NativeCell);
                cell.Row = Place(row, block, capacity, ref at, out cell.RowLength);
                cell.ColumnFamily = Place(family, block, capacity, ref at, out cell.ColumnFamilyLength);
                cell.ColumnQualifier = Place(qualifier, block, capacity, ref at, out cell.ColumnQualifierLength);
                cell.Value = value;
                cell.ValueLength = valueLength;
                cell.Timestamp = key.Timestamp;
                cell.Flag = (byte)cellFlag;

                if (!generate)
                {
                    lock (this.syncRoot)
                    {
                        var current = this.Handle;
                        Native.Check(Native.Table.MutSet(current, &cell));
                    }

                    return;
                }

                // the generated key is handed back so the caller can read it off the key
                NativeBuffer buffer;
                lock (this.syncRoot)
                {
                    var current = this.Handle;
                    Native.Check(Native.Table.MutSetGen(current, &cell, &buffer));
                }

                key.Row = Native.TakeString(ref buffer);
            }
        }

        private unsafe void Remove(string row, string columnFamily, string columnQualifier, ulong timestamp)
        {
            var bound = Bound(row) + Bound(columnFamily) + Bound(columnQualifier);
            var spill = bound > StackLimit ? new byte[bound] : null;

            var stack = stackalloc byte[StackLimit];
            fixed (byte* pinned = spill)
            {
                var block = spill != null ? pinned : stack;
                var capacity = spill != null ? bound : StackLimit;
                var at = 0;

                var cell = default(NativeCell);
                cell.Row = Place(row, block, capacity, ref at, out cell.RowLength);
                cell.ColumnFamily = Place(columnFamily, block, capacity, ref at, out cell.ColumnFamilyLength);
                cell.ColumnQualifier = Place(columnQualifier, block, capacity, ref at, out cell.ColumnQualifierLength);
                cell.Value = null;
                cell.ValueLength = 0;
                cell.Timestamp = timestamp;

                // the native side narrows this to the coarsest delete the key supports
                cell.Flag = Abi.CellFlagDeleteCell;

                lock (this.syncRoot)
                {
                    var current = this.Handle;
                    Native.Check(Native.Table.MutDel(current, &cell));
                }
            }
        }
    }
}
