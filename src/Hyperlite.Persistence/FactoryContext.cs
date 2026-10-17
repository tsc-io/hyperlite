namespace Hyperlite.Persistence
{
    using System;
    using System.Collections.Generic;


    using NamespaceDictionary =
        Hyperlite.Persistence.Collections.Concurrent.ConcurrentStringDictionary<INamespace>;
    using TableDictionary =
        Hyperlite.Persistence.Collections.Concurrent.ConcurrentStringDictionary<ITable>;

    /// <summary>
    ///     The entity manager factory context.
    /// </summary>
    internal sealed class FactoryContext : IDisposable
    {
        private readonly IClient client;

        private readonly PersistenceConfiguration configuration;

        private readonly IContext context;

        /// <summary>
        ///     Indicating whether this factory context owns the database context.
        /// </summary>
        private readonly bool disposeContext;

        /// <summary>
        ///     Maps namespace names to database namespace instances.
        /// </summary>
        private readonly NamespaceDictionary namespaces = new NamespaceDictionary();

        /// <summary>
        ///     Maps full table names to database table instances.
        /// </summary>
        private readonly TableDictionary tables = new TableDictionary();

        /// <summary>
        ///     Indicating whether this factory context has been disposed.
        /// </summary>
        private bool disposed;

        /// <param name="context">
        ///     The database context.
        /// </param>
        /// <param name="disposeContext">
        ///     Flag indicating whether this factory context owns the database context.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="configuration" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="context" /> is null.
        /// </exception>
        internal FactoryContext(PersistenceConfiguration configuration, IContext context, bool disposeContext)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            this.configuration = configuration;
            this.context = context;
            this.disposeContext = disposeContext;
            this.client = context.CreateClient();
        }

        /// <summary>
        ///     Do not dispose the returned database client, it's owned by the factory context.
        /// </summary>
        /// <value>
        ///     The database client.
        /// </value>
        internal IClient Client
        {
            get
            {
                this.ThrowIfDisposed();
                return this.client;
            }
        }

        internal PersistenceConfiguration Configuration
        {
            get
            {
                this.ThrowIfDisposed();
                return this.configuration;
            }
        }

        /// <summary>
        ///     Gets the database context properties.
        /// </summary>
        /// <value>
        ///     The database context properties.
        /// </value>
        internal IDictionary<string, object> Properties
        {
            get
            {
                this.ThrowIfDisposed();
                return this.context != null ? this.context.Properties : new Dictionary<string, object>();
            }
        }

        /// <value>
        ///     The database root namespace.
        /// </value>
        internal INamespace RootNamespace
        {
            get
            {
                this.ThrowIfDisposed();
                return this.GetNamespace(this.configuration.RootNamespace);
            }
        }

        /// <value>
        ///     The synchronization object.
        /// </value>
        internal object SyncRoot { get; } = new object();

        public void Dispose()
        {
            this.Dispose(true);
        }

        /// <summary>
        ///     Clears the entire factory context.
        /// </summary>
        internal void Clear()
        {
            this.ThrowIfDisposed();

            foreach (var table in this.tables.Values)
            {
                table.Dispose();
            }

            this.tables.Clear();

            foreach (var ns in this.namespaces.Values)
            {
                ns.Dispose();
            }

            this.namespaces.Clear();
        }

        /// <param name="ns">
        ///     The namespace name.
        /// </param>
        internal string GetFullyQualifiedTableName(string ns, string tableName)
        {
            if (!string.IsNullOrEmpty(ns))
            {
                if (ns[ns.Length - 1] != '/')
                {
                    ns += '/';
                }

                if (ns[0] != '/')
                {
                    ns = ns.Insert(0, this.configuration.RootNamespace);
                }

                return ns + tableName;
            }

            return this.configuration.RootNamespace + tableName;
        }

        /// <summary>
        ///     Gets the database namespace instance for the namespace name specified.
        /// </summary>
        /// <param name="ns">
        ///     The namespace name.
        /// </param>
        /// <returns>
        ///     The database namespace instance for the namespace name specified.
        /// </returns>
        /// <remarks>
        ///     Do not dispose the returned namespace instance, it's owned by the factory context.
        /// </remarks>
        internal INamespace GetNamespace(string ns)
        {
            this.ThrowIfDisposed();

            if (!string.IsNullOrEmpty(ns))
            {
                if (ns != "/")
                {
                    ns = ns.TrimEnd('/');
                    if (ns[0] != '/')
                    {
                        ns = ns.Insert(0, this.configuration.RootNamespace);
                    }
                }
            }
            else
            {
                ns = this.configuration.RootNamespace;
                ns = ns.TrimEnd('/');
            }

            return this.namespaces.GetOrAdd(ns,
                _ns => this.client.OpenNamespace(_ns,
                    OpenDispositions.OpenAlways | OpenDispositions.CreateIntermediate));
        }

        /// <summary>
        ///     Gets the database table instance for the namespace/table name specified.
        /// </summary>
        /// <param name="ns">
        ///     The namespace name.
        /// </param>
        /// <returns>
        ///     The database table instance for the namespace/table name specified.
        /// </returns>
        /// <remarks>
        ///     Do not dispose the returned table instance, it's owned by the factory context.
        /// </remarks>
        internal ITable GetTable(string ns, string tableName)
        {
            this.ThrowIfDisposed();

            lock (this.SyncRoot)
            {
                return this.tables.GetOrAdd(
                    this.GetFullyQualifiedTableName(ns, tableName),
                    fullyQualifiedTableName => this.GetNamespace(ns).OpenTable(tableName, OpenDispositions.Force));
            }
        }

        private void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    this.Clear();

                    this.client.Dispose();
                    if (this.disposeContext)
                    {
                        this.context.Dispose();
                    }
                }

                this.disposed = true;
            }
        }

        /// <summary>
        ///     The throw if the object has been disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        ///     If the object has been already disposed.
        /// </exception>
        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException("Factory context has been already disposed");
            }
        }
    }
}
