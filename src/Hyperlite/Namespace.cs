namespace Hyperlite
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;

    using Hyperlite.Interop;

    /// <summary>
    /// Represents a namespace, provides methods to handle tables and nested
    /// namespaces.
    /// </summary>
    public sealed unsafe class Namespace : INamespace
    {
        private readonly object syncRoot = new object();

        private readonly Client client;

        private IntPtr handle;

        private bool disposed;

        internal Namespace(Client client, IntPtr handle)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (handle == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            this.client = client;
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
                    Native.Check(Native.Table.NsName(current, &buffer));
                    return Native.TakeString(ref buffer);
                }
            }
        }

        /// <inheritdoc />
        public IList<string> Namespaces
        {
            get
            {
                var listing = this.GetListing(false);
                var names = new List<string>(listing.Namespaces.Count);
                foreach (var item in listing.Namespaces)
                {
                    names.Add(item.Name);
                }

                return names;
            }
        }

        /// <inheritdoc />
        public IList<string> Tables
        {
            get
            {
                return this.GetListing(false).Tables;
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

        /// <summary>
        /// Taken by the client around a call passing <see cref="Handle" />, which Dispose then
        /// waits for.
        /// </summary>
        internal object SyncRoot => this.syncRoot;

        /// <summary>
        /// The native handle, for the client to pass as a base namespace, under
        /// <see cref="SyncRoot" />.
        /// </summary>
        internal IntPtr Handle
        {
            get
            {
                if (this.disposed || this.handle == IntPtr.Zero)
                {
                    throw new ObjectDisposedException(nameof(Namespace));
                }

                return this.handle;
            }
        }

        /// <inheritdoc />
        public void CreateTable(string name, string schema)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (string.IsNullOrEmpty(schema))
            {
                throw new ArgumentNullException(nameof(schema));
            }

            name = name.Trim('/');
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Invalid table name", nameof(name));
            }

            var nameBytes = Utf8.Encode(name);
            var schemaBytes = Utf8.Encode(schema);

            lock (this.syncRoot)
            {
                var current = this.Handle;
                fixed (byte* pinnedName = nameBytes)
                fixed (byte* pinnedSchema = schemaBytes)
                {
                    Native.Check(Native.Table.NsTblCreate(
                        current,
                        pinnedName,
                        nameBytes.Length - 1,
                        pinnedSchema,
                        schemaBytes.Length - 1));
                }
            }
        }

        /// <inheritdoc />
        public void CreateTable(string name, string schema, CreateDispositions dispo)
        {
            lock (this.syncRoot)
            {
                this.ThrowIfDisposed();

                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentNullException(nameof(name));
                }

                // trimmed as CreateTable(name, schema) trims it, so that the existence check looks
                // for the table that would be created
                name = name.Trim('/');
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException("Invalid table name", nameof(name));
                }

                // here, not only in CreateTable(name, schema): refused only there, after
                // CreateIntermediate, a missing schema left the namespaces behind
                if (string.IsNullOrEmpty(schema))
                {
                    throw new ArgumentNullException(nameof(schema));
                }

                if ((dispo & CreateDispositions.CreateIfNotExist) == CreateDispositions.CreateIfNotExist
                    && this.TableExists(name))
                {
                    return;
                }

                if ((dispo & CreateDispositions.CreateIntermediate) == CreateDispositions.CreateIntermediate)
                {
                    // A qualified table name implies the namespaces leading up to it. The name is
                    // checked first, as the native side checks a name given: refused only after
                    // them, it left them behind.
                    var basename = name.Substring(name.LastIndexOf('/') + 1);
                    if (name.Contains("//") || name.IndexOf('\0') >= 0
                        || basename.Length == 0 || basename[0] == '^')
                    {
                        throw new SyntaxException("Invalid table name");
                    }

                    var at = name.LastIndexOf('/');
                    if (at > 0)
                    {
                        this.CreateNamespace(
                            name.Substring(0, at),
                            CreateDispositions.CreateIntermediate | CreateDispositions.CreateIfNotExist);
                    }
                }

                this.CreateTable(name, schema);
            }
        }

        /// <inheritdoc />
        public void CreateTableLike(string name, string like)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (string.IsNullOrEmpty(like))
            {
                throw new ArgumentNullException(nameof(like));
            }

            // trimmed as CreateTable trims it
            name = name.Trim('/');
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Invalid table name", nameof(name));
            }

            var nameBytes = Utf8.Encode(name);
            var likeBytes = Utf8.Encode(like);

            lock (this.syncRoot)
            {
                var current = this.Handle;
                fixed (byte* pinnedName = nameBytes)
                fixed (byte* pinnedLike = likeBytes)
                {
                    Native.Check(Native.Table.NsTblCreateLike(
                        current, pinnedName, nameBytes.Length - 1, pinnedLike, likeBytes.Length - 1));
                }
            }
        }

        /// <inheritdoc />
        public void CreateTableLike(string name, string like, CreateDispositions dispo)
        {
            lock (this.syncRoot)
            {
                this.ThrowIfDisposed();

                // checked and trimmed as in CreateTable(name, schema, dispo)
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentNullException(nameof(name));
                }

                if (string.IsNullOrEmpty(like))
                {
                    throw new ArgumentNullException(nameof(like));
                }

                name = name.Trim('/');
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException("Invalid table name", nameof(name));
                }

                if ((dispo & CreateDispositions.CreateIfNotExist) == CreateDispositions.CreateIfNotExist
                    && this.TableExists(name))
                {
                    return;
                }

                this.CreateTableLike(name, like);
            }
        }

        /// <inheritdoc />
        public void AlterTable(string name, string schema)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (string.IsNullOrEmpty(schema))
            {
                throw new ArgumentNullException(nameof(schema));
            }

            var nameBytes = Utf8.Encode(name);
            var schemaBytes = Utf8.Encode(schema);

            lock (this.syncRoot)
            {
                var current = this.Handle;
                fixed (byte* pinnedName = nameBytes)
                fixed (byte* pinnedSchema = schemaBytes)
                {
                    Native.Check(Native.Table.NsTblAlter(
                        current, pinnedName, nameBytes.Length - 1, pinnedSchema, schemaBytes.Length - 1));
                }
            }
        }

        /// <inheritdoc />
        public void RenameTable(string nameOld, string nameNew)
        {
            if (string.IsNullOrEmpty(nameOld))
            {
                throw new ArgumentNullException(nameof(nameOld));
            }

            if (string.IsNullOrEmpty(nameNew))
            {
                throw new ArgumentNullException(nameof(nameNew));
            }

            if (nameOld == nameNew)
            {
                return;
            }

            var oldBytes = Utf8.Encode(nameOld);
            var newBytes = Utf8.Encode(nameNew);

            lock (this.syncRoot)
            {
                var current = this.Handle;
                fixed (byte* pinnedOld = oldBytes)
                fixed (byte* pinnedNew = newBytes)
                {
                    Native.Check(Native.Table.NsTblRename(
                        current, pinnedOld, oldBytes.Length - 1, pinnedNew, newBytes.Length - 1));
                }
            }
        }

        /// <inheritdoc />
        public ITable OpenTable(string name)
        {
            return this.OpenTable(name, OpenDispositions.OpenExisting);
        }

        /// <inheritdoc />
        public ITable OpenTable(string name, OpenDispositions dispo)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            return this.Open(name, dispo);
        }

        /// <inheritdoc />
        public ITable OpenTable(string name, string schema, OpenDispositions dispo)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (string.IsNullOrEmpty(schema))
            {
                throw new ArgumentNullException(nameof(schema));
            }

            // trimmed as CreateTable trims it, so that the table looked for is the one it creates
            name = name.Trim('/');
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Invalid table name", nameof(name));
            }

            lock (this.syncRoot)
            {
                this.ThrowIfDisposed();

                switch (dispo & (OpenDispositions.OpenAlways | OpenDispositions.CreateAlways))
                {
                    case OpenDispositions.OpenAlways:
                        if (!this.TableExists(name))
                        {
                            this.CreateTable(name, schema);
                        }

                        break;

                    case OpenDispositions.CreateAlways:
                        this.DropTable(name, DropDispositions.IfExists);
                        this.CreateTable(name, schema);
                        break;
                }

                return this.Open(name, dispo);
            }
        }

        /// <inheritdoc />
        public void DropTable(string name)
        {
            this.DropTable(name, DropDispositions.None);
        }

        /// <inheritdoc />
        public void DropTable(string name, DropDispositions dispo)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var flags = (dispo & DropDispositions.IfExists) == DropDispositions.IfExists
                ? Abi.FlagIfExists
                : 0;

            var bytes = Utf8.Encode(name);
            lock (this.syncRoot)
            {
                var current = this.Handle;
                fixed (byte* pinned = bytes)
                {
                    Native.Check(Native.Table.NsTblDrop(current, pinned, bytes.Length - 1, flags));
                }
            }
        }

        /// <inheritdoc />
        public void DropTables()
        {
            foreach (var table in this.Tables)
            {
                this.DropTable(table, DropDispositions.IfExists);
            }
        }

        /// <inheritdoc />
        public void DropTables(Regex regex, DropDispositions dispo)
        {
            if (regex == null)
            {
                throw new ArgumentNullException(nameof(regex));
            }

            foreach (var table in this.Tables)
            {
                if (regex.Match(table).Success)
                {
                    this.DropTable(table, dispo);
                }
            }
        }

        /// <inheritdoc />
        public bool TableExists(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var bytes = Utf8.Encode(name);
            lock (this.syncRoot)
            {
                var current = this.Handle;
                int exists;
                fixed (byte* pinned = bytes)
                {
                    Native.Check(Native.Table.NsTblExists(current, pinned, bytes.Length - 1, &exists));
                }

                return exists != 0;
            }
        }

        /// <inheritdoc />
        public string GetTableSchema(string name)
        {
            return this.GetTableSchema(name, false);
        }

        /// <inheritdoc />
        public string GetTableSchema(string name, bool withIds)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var flags = withIds ? Abi.FlagWithIds : 0;
            var bytes = Utf8.Encode(name);

            lock (this.syncRoot)
            {
                var current = this.Handle;
                NativeBuffer buffer;
                fixed (byte* pinned = bytes)
                {
                    Native.Check(Native.Table.NsTblSchema(
                        current, pinned, bytes.Length - 1, flags, &buffer));
                }

                return Native.TakeString(ref buffer);
            }
        }

        /// <inheritdoc />
        public void CreateNamespace(string name)
        {
            this.ThrowIfDisposedLocked();
            this.client.CreateNamespace(name, this);
        }

        /// <inheritdoc />
        public void CreateNamespace(string name, CreateDispositions dispo)
        {
            this.ThrowIfDisposedLocked();
            this.client.CreateNamespace(name, this, dispo);
        }

        /// <inheritdoc />
        public INamespace OpenNamespace(string name)
        {
            return this.OpenNamespace(name, OpenDispositions.OpenExisting);
        }

        /// <inheritdoc />
        public INamespace OpenNamespace(string name, OpenDispositions dispo)
        {
            this.ThrowIfDisposedLocked();
            return this.client.OpenNamespace(name, this, dispo);
        }

        /// <inheritdoc />
        public void DropNamespace(string name)
        {
            this.ThrowIfDisposedLocked();
            this.client.DropNamespace(name, this);
        }

        /// <inheritdoc />
        public void DropNamespace(string name, DropDispositions dispo)
        {
            this.ThrowIfDisposedLocked();
            this.client.DropNamespace(name, this, dispo);
        }

        /// <inheritdoc />
        public void DropNamespaces(DropDispositions dispo)
        {
            this.ThrowIfDisposedLocked();
            foreach (var ns in this.Namespaces)
            {
                this.client.DropNamespace(ns, this, DropDispositions.IfExists | dispo);
            }
        }

        /// <inheritdoc />
        public void DropNamespaces(Regex regex, DropDispositions dispo)
        {
            if (regex == null)
            {
                throw new ArgumentNullException(nameof(regex));
            }

            this.ThrowIfDisposedLocked();
            foreach (var ns in this.Namespaces)
            {
                if (regex.Match(ns).Success)
                {
                    this.client.DropNamespace(ns, this, DropDispositions.IfExists | dispo);
                }
            }
        }

        /// <inheritdoc />
        public bool NamespaceExists(string name)
        {
            this.ThrowIfDisposedLocked();
            return this.client.NamespaceExists(name, this);
        }

        /// <inheritdoc />
        public NamespaceListing GetListing()
        {
            return this.GetListing(false);
        }

        /// <inheritdoc />
        public NamespaceListing GetListing(bool deep)
        {
            var flags = deep ? Abi.FlagDeep : 0;

            byte[] encoded;
            lock (this.syncRoot)
            {
                var current = this.Handle;
                NativeBuffer buffer;
                Native.Check(Native.Table.NsListing(current, flags, &buffer));
                encoded = Native.TakeBytes(ref buffer);
            }

            return new NamespaceListing(this, ListingDecoder.Decode(encoded));
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
        /// Releases this namespace. Tables opened through it stay usable until they are
        /// disposed themselves.
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
                Native.Check(Native.Table.NsRelease(released));
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed || this.handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(Namespace));
            }
        }

        private void ThrowIfDisposedLocked()
        {
            lock (this.syncRoot)
            {
                this.ThrowIfDisposed();
            }
        }

        private ITable Open(string name, OpenDispositions dispo)
        {
            var flags = (dispo & OpenDispositions.Force) == OpenDispositions.Force ? Abi.FlagForce : 0;
            var bytes = Utf8.Encode(name);

            lock (this.syncRoot)
            {
                var current = this.Handle;
                IntPtr created;
                fixed (byte* pinned = bytes)
                {
                    Native.Check(Native.Table.NsTblOpen(
                        current, pinned, bytes.Length - 1, flags, &created));
                }

                return new Table(this, created);
            }
        }
    }
}
