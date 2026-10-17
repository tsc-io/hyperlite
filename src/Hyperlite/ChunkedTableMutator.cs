namespace Hyperlite
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;

    using Hyperlite.Interop;

    /// <summary>
    /// A mutator that accumulates cells and writes them in chunks.
    /// </summary>
    /// <remarks>
    /// Safe to share between threads. The chunk is filled and drained under a lock of its
    /// own, and a drained chunk is written under a second lock, held from taking the chunk
    /// until it is written: chunks go over in the order they were taken, so every cell keeps
    /// the order it was accepted in, and a <see cref="Flush" /> waits for a chunk another
    /// thread is still writing.
    ///
    /// A chunk goes over as soon as either the accumulated value size or the cell count
    /// reaches its limit, and whatever is left goes on <see cref="Flush" /> or on
    /// dispose. Where FlushEachChunk is set, each chunk is also committed.
    ///
    /// Note what this changes about deletes: a chunked mutator turns every Delete into an
    /// accumulated cell carrying a delete flag, so deletes and writes keep their relative
    /// order within a chunk. The regular mutator issues deletes immediately.
    /// </remarks>
    public sealed class ChunkedTableMutator : TableMutator
    {
        private readonly object chunkSync = new object();

        private readonly object writeSync = new object();

        private readonly uint maxChunkSize;

        private readonly uint maxCellCount;

        private readonly bool flushEachChunk;

        private readonly List<Cell> chunk;

        private long accumulated;

        // set under chunkSync once disposing begins: a cell accepted after the last chunk was
        // written would never be written at all
        private bool closed;

        internal ChunkedTableMutator(
            IntPtr handle,
            uint maxChunkSize,
            uint maxCellCount,
            bool flushEachChunk)
            : base(handle)
        {
            this.maxChunkSize = maxChunkSize;
            this.maxCellCount = maxCellCount;
            this.flushEachChunk = flushEachChunk;

            // the same cap ht4n applied, so a silly MaxCellCount does not preallocate
            // hundreds of megabytes of list
            var capacity = (int)Math.Min(maxCellCount, 64u * 1024u);
            this.chunk = new List<Cell>(Math.Max(capacity, 16));
        }

        /// <inheritdoc />
        public override void Set(IEnumerable<Cell> cells, bool createRowKey)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            foreach (var cell in CheckedBatch(cells))
            {
                this.Accumulate(cell.Key, cell.Value, cell.Flag, createRowKey);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Accumulated rather than issued straight away, so a delete keeps its place
        /// relative to the writes around it in the same chunk.
        /// </remarks>
        public override void Delete(string row)
        {
            if (string.IsNullOrEmpty(row))
            {
                throw new ArgumentException("Invalid parameter row (null or empty)", nameof(row));
            }

            this.Set(new Cell(new Key(row), CellFlag.DeleteRow));
        }

        /// <inheritdoc />
        public override void Delete(Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Set(new Cell(key, Cell.DeleteFlagFromKey(key), true));
        }

        /// <inheritdoc />
        public override void Delete(IEnumerable<Key> keys)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            foreach (var key in keys)
            {
                if (key != null)
                {
                    this.Delete(key);
                }
            }
        }

        /// <inheritdoc />
        public override void Delete(IEnumerable<Cell> cells)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            foreach (var cell in cells)
            {
                if (cell != null && cell.Key != null)
                {
                    this.Delete(cell.Key);
                }
            }
        }

        /// <inheritdoc />
        public override void Flush()
        {
            // where a chunk was written and committed there is nothing left to flush
            if (!this.WriteChunk(true))
            {
                base.Flush();
            }
        }

        /// <inheritdoc />
        protected override void Set(Key key, byte[] value, CellFlag cellFlag, bool createRowKey)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Accumulate(key, value, cellFlag, createRowKey);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    bool first;
                    lock (this.chunkSync)
                    {
                        first = !this.closed;
                        this.closed = true;
                    }

                    if (first)
                    {
                        this.WriteChunk(true);
                    }
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Copied into the chunk, so that it keeps its place among the cells around it.
        /// </remarks>
        internal override unsafe void Set(Key key, byte* value, int valueLength)
        {
            // before the copy allocates what is refused anyway
            if (valueLength > NativeLimits.MaxCellSize)
            {
                throw new ArgumentException("cell value exceeds the limit", "valueLength");
            }

            byte[] copy = null;
            if (value != null)
            {
                copy = new byte[valueLength];
                Marshal.Copy((IntPtr)value, copy, 0, valueLength);
            }

            this.Accumulate(key, copy, CellFlag.Default, false);
        }

        /// <summary>
        /// Adds a cell to the pending chunk, writing the chunk out once it is full.
        /// </summary>
        private void Accumulate(Key key, byte[] value, CellFlag cellFlag, bool createRowKey)
        {
            if (value != null && value.Length > NativeLimits.MaxCellSize)
            {
                throw new ArgumentException("cell value exceeds the limit", "valueLength");
            }

            // refused here, as the regular mutator refuses it: written with the chunk it would
            // take the chunk's other cells down with it
            if (cellFlag != CellFlag.Default && string.IsNullOrEmpty(key.Row))
            {
                throw new BadKeyException("Invalid row key - empty");
            }

            // checked before the key is touched, so a refused call leaves the caller's key alone
            this.ThrowIfClosed();

            // for a cell written, never for a delete, see TableMutator.Set(IEnumerable<Cell>, bool)
            if (cellFlag == CellFlag.Default && (createRowKey || string.IsNullOrEmpty(key.Row)))
            {
                key.Row = Base85.Generate();
            }

            lock (this.chunkSync)
            {
                if (this.closed)
                {
                    throw new ObjectDisposedException(nameof(ChunkedTableMutator));
                }

                // the key is cloned: the caller is free to reuse the instance, and the
                // chunk has to remember what it was told at this point
                this.chunk.Add(new Cell((Key)key.Clone(), value, cellFlag));
                this.accumulated += value != null ? value.Length : 0;
            }

            this.WriteChunk(false);
        }

        /// <summary>
        /// Writes the pending chunk when it is full, or when forced.
        /// </summary>
        /// <returns>true when the chunk was written and committed.</returns>
        private bool WriteChunk(bool force)
        {
            // the common case, a chunk not yet full, without the write lock
            if (!force && !this.IsFull())
            {
                return false;
            }

            lock (this.writeSync)
            {
                List<Cell> pending;
                lock (this.chunkSync)
                {
                    if ((!force && !this.IsFullLocked()) || this.chunk.Count == 0)
                    {
                        return false;
                    }

                    pending = new List<Cell>(this.chunk);
                    this.chunk.Clear();
                    this.accumulated = 0;
                }

                // the row keys are already settled, so the batch goes over as it is
                base.Set(pending, false);

                if (this.flushEachChunk)
                {
                    base.Flush();
                    return true;
                }

                return false;
            }
        }

        private bool IsFull()
        {
            lock (this.chunkSync)
            {
                return this.IsFullLocked();
            }
        }

        private bool IsFullLocked()
        {
            return this.accumulated >= this.maxChunkSize || this.chunk.Count >= this.maxCellCount;
        }

        private void ThrowIfClosed()
        {
            lock (this.chunkSync)
            {
                if (this.closed)
                {
                    throw new ObjectDisposedException(nameof(ChunkedTableMutator));
                }
            }
        }
    }
}
