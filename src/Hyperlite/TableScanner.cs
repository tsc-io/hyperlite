namespace Hyperlite
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    using Hyperlite.Interop;

    /// <summary>
    /// Represents a table scanner, reads cells from a table.
    /// </summary>
    public sealed unsafe class TableScanner : ITableScanner
    {
        private readonly object syncRoot = new object();

        private readonly ScanSpec scanSpec;

        private IntPtr handle;

        private bool disposed;

        /// <summary>
        /// Set while Next hands an action the native cell, which a Dispose from within the
        /// action must not free under it.
        /// </summary>
        private bool lending;

        internal TableScanner(IntPtr handle, ScanSpec scanSpec)
        {
            if (handle == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            this.handle = handle;
            this.scanSpec = scanSpec;
        }

        /// <inheritdoc />
        public ScanSpec ScanSpec
        {
            get
            {
                return this.scanSpec;
            }
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
        public bool Move(Cell cell)
        {
            if (cell == null)
            {
                throw new ArgumentNullException(nameof(cell));
            }

            lock (this.syncRoot)
            {
                NativeCell native;
                if (!this.Step(out native))
                {
                    return false;
                }

                cell.From(ref native);
                return true;
            }
        }

        /// <inheritdoc />
        public bool Move(BufferedCell cell)
        {
            if (cell == null)
            {
                throw new ArgumentNullException(nameof(cell));
            }

            lock (this.syncRoot)
            {
                NativeCell native;
                if (!this.Step(out native))
                {
                    return false;
                }

                cell.From(ref native);
                return true;
            }
        }

        /// <inheritdoc />
        public bool Move(PooledCell cell)
        {
            if (cell == null)
            {
                throw new ArgumentNullException(nameof(cell));
            }

            lock (this.syncRoot)
            {
                NativeCell native;
                if (!this.Step(out native))
                {
                    return false;
                }

                cell.From(ref native);
                return true;
            }
        }

        /// <inheritdoc />
        public bool Next(out Cell cell)
        {
            lock (this.syncRoot)
            {
                NativeCell native;
                if (!this.Step(out native))
                {
                    cell = null;
                    return false;
                }

                cell = new Cell();
                cell.From(ref native);
                return true;
            }
        }

        /// <inheritdoc />
        public bool Next(Func<Key, IntPtr, int, bool> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var released = IntPtr.Zero;
            try
            {
                lock (this.syncRoot)
                {
                    NativeCell native;
                    if (!this.Step(out native))
                    {
                        return false;
                    }

                    var key = new Key();
                    key.From(ref native);

                    // The pointer is into the scanner's current row and is only good until the
                    // next step, which is why the contract says "for the duration of the call".
                    // The lock is reentrant, so the action can dispose the scanner: then the
                    // release waits for the action to return.
                    this.lending = true;
                    try
                    {
                        return action(key, (IntPtr)native.Value, native.ValueLength);
                    }
                    finally
                    {
                        this.lending = false;
                        if (this.disposed)
                        {
                            released = this.handle;
                            this.handle = IntPtr.Zero;
                        }
                    }
                }
            }
            finally
            {
                if (released != IntPtr.Zero)
                {
                    Native.Check(Native.Table.ScnRelease(released));
                }
            }
        }

        /// <summary>
        /// Returns an enumerator over the remaining cells.
        /// </summary>
        public IEnumerator<Cell> GetEnumerator()
        {
            lock (this.syncRoot)
            {
                this.ThrowIfDisposed();
            }

            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        /// <summary>
        /// Releases this scanner.
        /// </summary>
        public void Dispose()
        {
            IntPtr released;
            lock (this.syncRoot)
            {
                if (this.disposed)
                {
                    return;
                }

                this.disposed = true;
                if (this.lending)
                {
                    // from within an action Next is running, which releases it on return
                    GC.SuppressFinalize(this);
                    return;
                }

                released = this.handle;
                this.handle = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);

            if (released != IntPtr.Zero)
            {
                Native.Check(Native.Table.ScnRelease(released));
            }
        }

        /// <summary>
        /// Advances the scan. Called under the lock.
        /// </summary>
        private bool Step(out NativeCell cell)
        {
            this.ThrowIfDisposed();

            NativeCell native;
            int eos;
            Native.Check(Native.Table.ScnNext(this.handle, &native, &eos));

            if (eos != 0)
            {
                cell = default(NativeCell);
                return false;
            }

            cell = native;
            return true;
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed || this.handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(TableScanner));
            }
        }

        /// <summary>
        /// Forward only enumerator over a scanner. A scan is single pass, so
        /// <see cref="Reset" /> is not supported.
        /// </summary>
        private sealed class Enumerator : IEnumerator<Cell>
        {
            private readonly TableScanner scanner;

            private Cell current;

            internal Enumerator(TableScanner scanner)
            {
                this.scanner = scanner;
            }

            public Cell Current
            {
                get
                {
                    return this.current;
                }
            }

            object IEnumerator.Current
            {
                get
                {
                    return this.current;
                }
            }

            public bool MoveNext()
            {
                Cell cell;
                if (!this.scanner.Next(out cell))
                {
                    this.current = null;
                    return false;
                }

                this.current = cell;
                return true;
            }

            public void Reset()
            {
                throw new InvalidOperationException("Unable to reset table scanner enumerator");
            }

            public void Dispose()
            {
            }
        }
    }
}
