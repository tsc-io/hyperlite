namespace Hyperlite
{
    using System;
    using System.Globalization;

    using Hyperlite.Interop;

    /// <summary>
    /// Represents a client, provides methods to handle namespaces.
    /// </summary>
    public sealed unsafe class Client : IClient
    {
        private readonly object syncRoot = new object();

        private readonly Context context;

        private IntPtr handle;

        private bool disposed;

        internal Client(Context context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            this.context = context;

            this.handle = context.Environment.CreateClient();
        }

        /// <inheritdoc />
        public IContext Context
        {
            get
            {
                return this.context;
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
        public void CreateNamespace(string name)
        {
            this.CreateNamespace(name, null, CreateDispositions.None);
        }

        /// <inheritdoc />
        public void CreateNamespace(string name, CreateDispositions dispo)
        {
            this.CreateNamespace(name, null, dispo);
        }

        /// <inheritdoc />
        public void CreateNamespace(string name, INamespace nsBase)
        {
            this.CreateNamespace(name, nsBase, CreateDispositions.None);
        }

        /// <inheritdoc />
        public void CreateNamespace(string name, INamespace nsBase, CreateDispositions dispo)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var typed = this.RequireNamespace(nsBase);

            var flags = 0;
            if ((dispo & CreateDispositions.CreateIntermediate) == CreateDispositions.CreateIntermediate)
            {
                flags |= Abi.FlagCreateIntermediate;
            }

            if ((dispo & CreateDispositions.CreateIfNotExist) == CreateDispositions.CreateIfNotExist)
            {
                flags |= Abi.FlagIfNotExists;
            }

            var bytes = Utf8.Encode(name);
            lock (this.BaseLock(typed))
                lock (this.syncRoot)
                {
                    var current = this.Handle;
                    var baseHandle = typed != null ? typed.Handle : IntPtr.Zero;
                    fixed (byte* pinned = bytes)
                    {
                        Native.Check(Native.Table.CliNsCreate(
                            current, pinned, bytes.Length - 1, baseHandle, flags));
                    }
                }
        }

        /// <inheritdoc />
        public INamespace OpenNamespace(string name)
        {
            return this.OpenNamespace(name, null, OpenDispositions.OpenExisting);
        }

        /// <inheritdoc />
        public INamespace OpenNamespace(string name, OpenDispositions dispo)
        {
            return this.OpenNamespace(name, null, dispo);
        }

        /// <inheritdoc />
        public INamespace OpenNamespace(string name, INamespace nsBase)
        {
            return this.OpenNamespace(name, nsBase, OpenDispositions.OpenExisting);
        }

        /// <inheritdoc />
        public INamespace OpenNamespace(string name, INamespace nsBase, OpenDispositions dispo)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            this.RequireNamespace(nsBase);

            var createIntermediate =
                (dispo & OpenDispositions.CreateIntermediate) == OpenDispositions.CreateIntermediate
                    ? CreateDispositions.CreateIntermediate
                    : CreateDispositions.None;

            switch (dispo & (OpenDispositions.OpenAlways | OpenDispositions.CreateAlways))
            {
                case OpenDispositions.OpenAlways:
                    if (!this.NamespaceExists(name, nsBase))
                    {
                        this.CreateNamespace(name, nsBase, createIntermediate);
                    }

                    break;

                case OpenDispositions.CreateAlways:
                    if (this.NamespaceExists(name, nsBase))
                    {
                        // an existing namespace is emptied rather than dropped and
                        // recreated, so an open handle on it stays valid
                        var existing = this.Open(name, nsBase);
                        try
                        {
                            existing.DropTables();
                            existing.DropNamespaces(DropDispositions.Complete);
                            return existing;
                        }
                        catch (Exception)
                        {
                            existing.Dispose();
                            throw;
                        }
                    }

                    this.CreateNamespace(name, nsBase, createIntermediate);
                    break;
            }

            return this.Open(name, nsBase);
        }

        /// <inheritdoc />
        public void DropNamespace(string name)
        {
            this.DropNamespace(name, null, DropDispositions.None);
        }

        /// <inheritdoc />
        public void DropNamespace(string name, DropDispositions dispo)
        {
            this.DropNamespace(name, null, dispo);
        }

        /// <inheritdoc />
        public void DropNamespace(string name, INamespace nsBase)
        {
            this.DropNamespace(name, nsBase, DropDispositions.None);
        }

        /// <inheritdoc />
        public void DropNamespace(string name, INamespace nsBase, DropDispositions dispo)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if ((nsBase == null || string.IsNullOrEmpty(nsBase.Name)) && name == "/")
            {
                throw new ArgumentException("Cannot drop root namespace", nameof(name));
            }

            var typed = this.RequireNamespace(nsBase);

            var flags = 0;
            if ((dispo & DropDispositions.IfExists) == DropDispositions.IfExists)
            {
                flags |= Abi.FlagIfExists;
            }

            if ((dispo & DropDispositions.IncludeTables) == DropDispositions.IncludeTables)
            {
                flags |= Abi.FlagDropTables;
            }

            if ((dispo & DropDispositions.Deep) == DropDispositions.Deep)
            {
                flags |= Abi.FlagDeep;
            }

            var bytes = Utf8.Encode(name);
            lock (this.BaseLock(typed))
                lock (this.syncRoot)
                {
                    var current = this.Handle;
                    var baseHandle = typed != null ? typed.Handle : IntPtr.Zero;
                    fixed (byte* pinned = bytes)
                    {
                        Native.Check(Native.Table.CliNsDrop(
                            current, pinned, bytes.Length - 1, baseHandle, flags));
                    }
                }
        }

        /// <inheritdoc />
        public bool NamespaceExists(string name)
        {
            return this.NamespaceExists(name, null);
        }

        /// <inheritdoc />
        public bool NamespaceExists(string name, INamespace nsBase)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var typed = this.RequireNamespace(nsBase);

            var bytes = Utf8.Encode(name);
            lock (this.BaseLock(typed))
                lock (this.syncRoot)
                {
                    var current = this.Handle;
                    var baseHandle = typed != null ? typed.Handle : IntPtr.Zero;
                    int exists;
                    fixed (byte* pinned = bytes)
                    {
                        Native.Check(Native.Table.CliNsExists(
                            current, pinned, bytes.Length - 1, baseHandle, &exists));
                    }

                    return exists != 0;
                }
        }

        /// <inheritdoc />
        public void Optimize()
        {
            lock (this.syncRoot)
            {
                this.ThrowIfDisposed();
                this.context.Environment.Optimize();
            }
        }

        /// <summary>
        /// Releases the client. Namespaces opened through it stay usable for their tables
        /// until they are disposed themselves; what they do with namespaces - creating, opening,
        /// dropping, testing for one - needs the client, and throws
        /// <see cref="ObjectDisposedException" />.
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
                Native.Check(Native.Table.CliRelease(released));
            }
        }

        private IntPtr Handle
        {
            get
            {
                this.ThrowIfDisposed();
                return this.handle;
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed || this.handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(Client));
            }
        }

        /// <summary>
        /// Validates a base namespace.
        /// </summary>
        /// <returns>The namespace, or null for the root.</returns>
        /// <remarks>
        /// Its handle is read under its lock, see BaseLock, and used before that is released:
        /// read before, a namespace disposed meanwhile would hand native a freed handle.
        /// </remarks>
        private Namespace RequireNamespace(INamespace nsBase)
        {
            lock (this.syncRoot)
            {
                this.ThrowIfDisposed();
            }

            if (nsBase == null)
            {
                return null;
            }

            var typed = nsBase as Namespace;
            if (typed == null)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Base namespace type, {0} expected",
                        typeof(Namespace)),
                    nameof(nsBase));
            }

            return typed;
        }

        /// <summary>
        /// What to lock, before the client, for a call on a base namespace: the namespace, so
        /// that it is not disposed while native uses its handle.
        /// </summary>
        private object BaseLock(Namespace nsBase)
        {
            return nsBase != null ? nsBase.SyncRoot : this.syncRoot;
        }

        private INamespace Open(string name, INamespace nsBase)
        {
            var typed = this.RequireNamespace(nsBase);
            var bytes = Utf8.Encode(name);

            lock (this.BaseLock(typed))
                lock (this.syncRoot)
                {
                    var current = this.Handle;
                    var baseHandle = typed != null ? typed.Handle : IntPtr.Zero;
                    IntPtr created;
                    fixed (byte* pinned = bytes)
                    {
                        Native.Check(Native.Table.CliNsOpen(
                            current, pinned, bytes.Length - 1, baseHandle, &created));
                    }

                    return new Namespace(this, created);
                }
        }
    }
}
