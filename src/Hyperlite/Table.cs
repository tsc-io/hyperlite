namespace Hyperlite
{
    using System;
    using System.Globalization;

    using Hyperlite.Interop;

    /// <summary>
    /// Represents a table, provides methods to read and write cells.
    /// </summary>
    public sealed unsafe class Table : ITable
    {
        private readonly object syncRoot = new object();

        private readonly Namespace ns;

        private IntPtr handle;

        private bool disposed;

        internal Table(Namespace ns, IntPtr handle)
        {
            if (ns == null)
            {
                throw new ArgumentNullException(nameof(ns));
            }

            if (handle == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            this.ns = ns;
            this.handle = handle;
        }

        /// <inheritdoc />
        public string Name
        {
            get
            {
                lock (this.syncRoot)
                {
                    var current = this.Handle;
                    NativeBuffer buffer;
                    Native.Check(Native.Table.TblName(current, &buffer));
                    return Native.TakeString(ref buffer);
                }
            }
        }

        /// <inheritdoc />
        public string Schema
        {
            get
            {
                return this.GetSchema(false);
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
        public ITableMutator CreateMutator()
        {
            return this.CreateMutator(null);
        }

        /// <inheritdoc />
        public ITableMutator CreateMutator(MutatorSpec mutatorSpec)
        {
            var timeout = 0;
            var flags = (int)MutatorFlags.Default;
            var flushInterval = 0;

            if (mutatorSpec == null)
            {
                return new TableMutator(this.CreateNativeMutator(timeout, flags, flushInterval));
            }

            if (mutatorSpec.Timeout.Ticks != 0)
            {
                if (mutatorSpec.Timeout.TotalMilliseconds < 0)
                {
                    throw new ArgumentException(
                        "Invalid parameter mutatorSpec (Timeout < 0)", nameof(mutatorSpec));
                }

                timeout = (int)Math.Min(mutatorSpec.Timeout.TotalMilliseconds, int.MaxValue);
            }

            if (mutatorSpec.FlushInterval.Ticks != 0)
            {
                if (mutatorSpec.FlushInterval.TotalMilliseconds < 0)
                {
                    throw new ArgumentException(
                        "Invalid parameter mutatorSpec (FlushInterval < 0)", nameof(mutatorSpec));
                }

                flushInterval = (int)Math.Min(mutatorSpec.FlushInterval.TotalMilliseconds, int.MaxValue);
            }

            flags = (int)mutatorSpec.Flags;

            ITableMutator mutator;
            switch (mutatorSpec.MutatorKind)
            {
                case MutatorKind.Chunked:
                    mutator = new ChunkedTableMutator(
                        this.CreateNativeMutator(timeout, flags, flushInterval),
                        mutatorSpec.MaxChunkSize,
                        mutatorSpec.MaxCellCount,
                        mutatorSpec.FlushEachChunk);
                    break;

                default:
                    mutator = new TableMutator(this.CreateNativeMutator(timeout, flags, flushInterval));
                    break;
            }

            if (mutatorSpec.Queued)
            {
                mutator = new QueuedTableMutator(mutator, mutatorSpec.Capacity);
            }

            return mutator;
        }

        /// <inheritdoc />
        public ITableScanner CreateScanner()
        {
            return this.CreateScanner(null);
        }

        /// <inheritdoc />
        public ITableScanner CreateScanner(ScanSpec scanSpec)
        {
            var timeout = 0;
            var flags = (int)ScannerFlags.Default;

            if (scanSpec != null)
            {
                if (scanSpec.Timeout.Ticks != 0)
                {
                    if (scanSpec.Timeout.TotalMilliseconds < 0)
                    {
                        throw new ArgumentException(
                            "Invalid parameter scanSpec (Timeout < 0)", nameof(scanSpec));
                    }

                    timeout = (int)Math.Min(scanSpec.Timeout.TotalMilliseconds, int.MaxValue);
                }

                flags = (int)scanSpec.Flags;
            }

            // the native scanner copies the spec, so the handle can go straight after
            using (var spec = new ScanSpecHandle())
            {
                if (scanSpec != null)
                {
                    scanSpec.To(spec);
                }

                lock (this.syncRoot)
                {
                    var current = this.Handle;
                    IntPtr created;
                    Native.Check(Native.Table.TblScnCreate(
                        current, spec.Handle, timeout, flags, &created));
                    return new TableScanner(created, scanSpec);
                }
            }
        }

        /// <inheritdoc />
        public Xml.TableSchema GetTableSchema()
        {
            return Xml.TableSchema.Parse(this.GetSchema(true));
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            // one lock around both, so a dispose in between cannot make it throw
            lock (this.syncRoot)
            {
                if (this.disposed || this.handle == IntPtr.Zero)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0}(disposed)", this.GetType());
                }

                return string.Format(
                    CultureInfo.InvariantCulture, "{0}(Name={1})", this.GetType(), this.Name ?? "null");
            }
        }

        /// <summary>
        /// Releases this table. Mutators and scanners created on it stay usable until they
        /// are disposed themselves.
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
                released = this.handle;
                this.handle = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);

            if (released != IntPtr.Zero)
            {
                Native.Check(Native.Table.TblRelease(released));
            }
        }

        private IntPtr Handle
        {
            get
            {
                if (this.disposed || this.handle == IntPtr.Zero)
                {
                    throw new ObjectDisposedException(nameof(Table));
                }

                return this.handle;
            }
        }


        private string GetSchema(bool withIds)
        {
            var flags = withIds ? Abi.FlagWithIds : 0;
            lock (this.syncRoot)
            {
                var current = this.Handle;
                NativeBuffer buffer;
                Native.Check(Native.Table.TblSchema(current, flags, &buffer));
                return Native.TakeString(ref buffer);
            }
        }

        private IntPtr CreateNativeMutator(int timeout, int flags, int flushInterval)
        {
            lock (this.syncRoot)
            {
                var current = this.Handle;
                IntPtr created;
                Native.Check(Native.Table.TblMutCreate(
                    current, timeout, flags, flushInterval, &created));
                return created;
            }
        }
    }
}
