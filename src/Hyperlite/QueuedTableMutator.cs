namespace Hyperlite
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Runtime.ExceptionServices;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    using Hyperlite.Interop;

    /// <summary>
    /// Wraps another mutator and hands its work to a background thread.
    /// </summary>
    /// <remarks>
    /// Every cell is cloned on the way in: the consumer runs on another thread and the
    /// caller is free to reuse the instance it passed. <see cref="Flush" /> waits until
    /// the queue has drained before flushing the inner mutator, and disposing waits for
    /// the worker to finish.
    ///
    /// A failure on the worker thread is remembered and rethrown from the next call, so
    /// it cannot be lost.
    /// </remarks>
    public sealed class QueuedTableMutator : ITableMutator
    {
        private readonly BlockingCollection<Cell> queue;

        // guards pending, and is what Flush waits on: the count and the wake-up under one lock,
        // so a Flush cannot see the queue drained while a cell is on its way in
        private readonly object drainSync = new object();

        // cancelled when the worker fails, so that a producer blocked on a full queue, which
        // nothing drains any more, gets the failure instead of waiting forever
        private readonly CancellationTokenSource failed = new CancellationTokenSource();

        private readonly ITableMutator inner;

        private readonly Task worker;

        private int pending;

        private ExceptionDispatchInfo workerFailure;

        private bool disposed;

        internal QueuedTableMutator(ITableMutator inner, int capacity)
        {
            if (inner == null)
            {
                throw new ArgumentNullException(nameof(inner));
            }

            this.inner = inner;
            this.queue = capacity > 0
                ? new BlockingCollection<Cell>(capacity)
                : new BlockingCollection<Cell>();

            this.worker = Task.Factory.StartNew(
                this.Consume,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        /// <inheritdoc />
        public bool IsDisposed
        {
            get
            {
                return this.disposed;
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
            this.ThrowIfDisposed();

            // before the key is touched: a failed worker refuses the call, see Enqueue
            this.ThrowIfWorkerFailed();
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (value != null && value.Length > NativeLimits.MaxCellSize)
            {
                throw new ArgumentException("cell value exceeds the limit", "valueLength");
            }

            if (createRowKey || string.IsNullOrEmpty(key.Row))
            {
                key.Row = Base85.Generate();
            }

            this.Enqueue(new Cell(key, value, true));
        }

        /// <inheritdoc />
        public void Set(Key key, IntPtr value, int valueLength)
        {
            this.ThrowIfDisposed();
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

            // before the copy below allocates what is refused anyway
            if (valueLength > NativeLimits.MaxCellSize)
            {
                throw new ArgumentException("cell value exceeds the limit", "valueLength");
            }

            // The queue hands the cell to another thread, which may well run after the
            // caller has released the unmanaged buffer, so this one has to copy.
            // Queuing and an unmanaged value do not combine.
            byte[] copy = null;
            if (valueLength > 0)
            {
                copy = new byte[valueLength];
                Marshal.Copy(value, copy, 0, valueLength);
            }

            this.Set(key, copy, false);
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
            this.ThrowIfDisposed();

            // before the key is touched: a failed worker refuses the call, see Enqueue
            this.ThrowIfWorkerFailed();
            if (cell == null)
            {
                throw new ArgumentNullException(nameof(cell));
            }

            var key = cell.Key;
            if (key == null)
            {
                throw new ArgumentException("Invalid parameter cell (cell.Key null)", nameof(cell));
            }

            // before the key is touched, so a refused call leaves it alone
            if (cell.Value != null && cell.Value.Length > NativeLimits.MaxCellSize)
            {
                throw new ArgumentException("cell value exceeds the limit", "valueLength");
            }

            // for a cell written, never for a delete, see TableMutator.Set(IEnumerable<Cell>, bool)
            if (cell.Flag == CellFlag.Default && (createRowKey || string.IsNullOrEmpty(key.Row)))
            {
                key.Row = Base85.Generate();
            }

            this.Enqueue(new Cell(key, cell.Value, cell.Flag, true));
        }

        /// <inheritdoc />
        public void Set(IEnumerable<Cell> cells)
        {
            this.Set(cells, false);
        }

        /// <inheritdoc />
        public void Set(IEnumerable<Cell> cells, bool createRowKey)
        {
            this.ThrowIfDisposed();

            // before the key is touched: a failed worker refuses the call, see Enqueue
            this.ThrowIfWorkerFailed();
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            foreach (var cell in TableMutator.CheckedBatch(cells))
            {
                var key = cell.Key;
                if (cell.Flag == CellFlag.Default && (createRowKey || string.IsNullOrEmpty(key.Row)))
                {
                    key.Row = Base85.Generate();
                }

                this.Enqueue(new Cell(key, cell.Value, cell.Flag, true));
            }
        }

        /// <inheritdoc />
        public void Delete(string row)
        {
            this.ThrowIfDisposed();
            if (string.IsNullOrEmpty(row))
            {
                throw new ArgumentException("Invalid parameter row (null or empty)", nameof(row));
            }

            this.Enqueue(new Cell(new Key(row), CellFlag.DeleteRow));
        }

        /// <inheritdoc />
        public void Delete(Key key)
        {
            this.ThrowIfDisposed();
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.Enqueue(new Cell(key, Cell.DeleteFlagFromKey(key), true));
        }

        /// <inheritdoc />
        public void Delete(IEnumerable<Key> keys)
        {
            this.ThrowIfDisposed();
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
        public void Delete(IEnumerable<Cell> cells)
        {
            this.ThrowIfDisposed();
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
        public void Flush()
        {
            this.ThrowIfDisposed();
            this.ThrowIfWorkerFailed();

            // wait for the queue to drain, then flush what the worker wrote
            lock (this.drainSync)
            {
                while (this.pending > 0 && this.workerFailure == null)
                {
                    Monitor.Wait(this.drainSync);
                }
            }

            // Checked again: the worker may have failed while this thread was waiting, and
            // the failure path is what released the wait. Flushing the inner mutator now
            // would hide the worker's exception behind whatever the flush does next.
            this.ThrowIfWorkerFailed();

            this.inner.Flush();
        }

        /// <summary>
        /// Drains the queue, flushes and releases the inner mutator.
        /// </summary>
        /// <remarks>
        /// Throws what the worker failed with, as every other call does, once everything is
        /// released - else the cells it did not write would be lost without a word. A failure
        /// of the inner mutator's own flush is thrown when the worker did not fail first.
        /// </remarks>
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;

            ExceptionDispatchInfo innerFailure = null;
            try
            {
                this.queue.CompleteAdding();

                try
                {
                    this.worker.Wait();
                }
                catch (AggregateException)
                {
                    // recorded by the worker, and thrown below
                }
            }
            finally
            {
                this.queue.Dispose();
                this.failed.Dispose();
                try
                {
                    this.inner.Dispose();
                }
                catch (Exception e)
                {
                    innerFailure = ExceptionDispatchInfo.Capture(e);
                }
            }

            this.ThrowIfWorkerFailed();
            innerFailure?.Throw();
        }

        private void Enqueue(Cell cell)
        {
            if (cell.Value != null && cell.Value.Length > NativeLimits.MaxCellSize)
            {
                throw new ArgumentException("cell value exceeds the limit", "valueLength");
            }

            // refused here, as the regular mutator refuses it: on the worker it would fail the
            // mutator for good
            if (cell.Flag != CellFlag.Default && string.IsNullOrEmpty(cell.Key.Row))
            {
                throw new BadKeyException("Invalid row key - empty");
            }

            this.ThrowIfWorkerFailed();

            lock (this.drainSync)
            {
                ++this.pending;
            }

            try
            {
                this.queue.Add(cell, this.failed.Token);
            }
            catch (Exception)
            {
                // not queued, whatever the reason - cancelled by a failed worker, or completed
                // or disposed by a Dispose under way - so not pending either, else a Flush
                // waits for it forever
                this.Drained();
                this.ThrowIfWorkerFailed();
                throw;
            }
        }

        private void Drained()
        {
            lock (this.drainSync)
            {
                if (--this.pending == 0)
                {
                    Monitor.PulseAll(this.drainSync);
                }
            }
        }

        private void Release()
        {
            lock (this.drainSync)
            {
                Monitor.PulseAll(this.drainSync);
            }
        }

        private void Consume()
        {
            try
            {
                foreach (var cell in this.queue.GetConsumingEnumerable())
                {
                    this.inner.Set(cell);
                    this.Drained();
                }

                this.Release();
            }
            catch (Exception e)
            {
                Logging.TraceException(e);
                this.workerFailure = ExceptionDispatchInfo.Capture(e);

                // release anyone waiting in Flush or blocked on a full queue, they will see
                // the failure instead
                this.Release();
                this.failed.Cancel();
                throw;
            }
        }

        private void ThrowIfWorkerFailed()
        {
            // thrown with the worker's stack, not this call's
            this.workerFailure?.Throw();
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(QueuedTableMutator));
            }
        }
    }
}
