namespace Hyperlite
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Reflection;

    using Hyperlite.Interop;

    /// <summary>
    /// Represents a database context. This is where a Hyperlite session starts.
    /// </summary>
    /// <example>
    /// <code>
    /// using (var context = Context.Create("Provider=SQLite; Uri=file://test.db"))
    /// using (var client = context.CreateClient())
    /// using (var ns = client.OpenNamespace("test", OpenDispositions.OpenAlways))
    /// {
    ///     // ...
    /// }
    /// </code>
    /// </example>
    public sealed class Context : IContext
    {
        private readonly IDictionary<string, object> properties;

        private readonly object instanceSync = new object();

        private EnvironmentHandle environment;

        private bool disposed;

        static Context()
        {
            var assembly = typeof(Context).Assembly;
            var name = assembly.GetName();

            Logging.TraceEvent(
                TraceEventType.Information,
                () => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} v{1} ({2}, {3})",
                    name.Name,
                    name.Version,
                    Attribute<AssemblyCompanyAttribute>(assembly, a => a.Company),
                    Attribute<AssemblyCopyrightAttribute>(assembly, a => a.Copyright)));
        }

        private Context(IDictionary<string, object> properties)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            this.properties = properties;

            var providerName = PropertyResolver.RequireProviderName(properties);
            if (!string.Equals(providerName, Config.ProviderSQLite, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "Unknown provider '{0}'", providerName), nameof(properties));
            }

            // The database is deliberately not opened here. ht4c created its provider
            // environment lazily, on the first createClient, so constructing a context
            // for a path that does not exist yet - or will never be opened, because the
            // caller only wanted to inspect the resolved properties - succeeds. Opening
            // eagerly would turn that into a failure.
        }

        /// <summary>
        /// Gets the resolved configuration properties.
        /// </summary>
        public IDictionary<string, object> Properties
        {
            get
            {
                return this.properties;
            }
        }

        /// <inheritdoc />
        public bool IsDisposed
        {
            get
            {
                lock (this.instanceSync)
                {
                    return this.disposed;
                }
            }
        }

        /// <summary>
        /// Creates a context from a connection string.
        /// </summary>
        /// <param name="connectionString">
        /// Either semicolon separated assignments ("Provider=SQLite; Uri=file://test.db")
        /// or command line style ("--Provider SQLite --Ht4n.SQLite.Filename test.db"), and
        /// the two may be mixed. "--config &lt;path&gt;" reads a name=value file first.
        /// </param>
        /// <returns>A new context. Dispose it when done.</returns>
        public static IContext Create(string connectionString)
        {
            if (connectionString == null)
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            return CreateProvider(MergeProperties(connectionString, null));
        }

        /// <summary>
        /// Creates a context from a property dictionary.
        /// </summary>
        /// <param name="properties">Configuration properties.</param>
        /// <returns>A new context. Dispose it when done.</returns>
        public static IContext Create(IDictionary<string, object> properties)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            return CreateProvider(MergeProperties(null, properties));
        }

        /// <summary>
        /// Creates a context from a connection string and a property dictionary. The
        /// connection string wins where both name a property.
        /// </summary>
        /// <param name="connectionString">Connection string.</param>
        /// <param name="properties">Configuration properties.</param>
        /// <returns>A new context. Dispose it when done.</returns>
        public static IContext Create(string connectionString, IDictionary<string, object> properties)
        {
            if (connectionString == null)
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            return CreateProvider(MergeProperties(connectionString, properties));
        }

        /// <summary>
        /// Determines whether the given provider is available in this build.
        /// </summary>
        /// <param name="contextKind">Provider to test.</param>
        /// <returns>true if available, otherwise false.</returns>
        /// <remarks>
        /// Hyperlite builds the SQLite provider only. The other kinds report false, which is
        /// what the ht4n test suite keys its provider specific tests off.
        /// </remarks>
        public static bool HasContext(ContextKind contextKind)
        {
            return contextKind == ContextKind.SQLite;
        }

        /// <inheritdoc />
        public IClient CreateClient()
        {
            this.ThrowIfDisposed();

            object uriValue;
            this.properties.TryGetValue(Config.Uri, out uriValue);
            var uriName = uriValue as string;
            if (string.IsNullOrEmpty(uriName))
            {
                throw new InvalidOperationException("Invalid uri property");
            }

            return new Client(this);
        }

        /// <summary>
        /// Releases the context. The connection closes once the clients, namespaces, tables,
        /// mutators and scanners obtained through it are disposed as well, see
        /// <see cref="IContext" />.
        /// </summary>
        public void Dispose()
        {
            lock (this.instanceSync)
            {
                if (this.disposed)
                {
                    return;
                }

                this.disposed = true;
            }

            if (this.environment != null)
            {
                this.environment.Dispose();
                this.environment = null;
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// The environment every client, namespace and table below this context works on,
        /// opened on first use.
        /// </summary>
        internal EnvironmentHandle Environment
        {
            get
            {
                lock (this.instanceSync)
                {
                    if (this.disposed)
                    {
                        throw new ObjectDisposedException(nameof(Context));
                    }

                    if (this.environment == null)
                    {
                        this.environment = EnvironmentHandle.Open(this.properties);
                    }

                    return this.environment;
                }
            }
        }

        internal void ThrowIfDisposed()
        {
            lock (this.instanceSync)
            {
                if (this.disposed)
                {
                    throw new ObjectDisposedException(nameof(Context));
                }
            }
        }

        /// <summary>
        /// Resolves the properties, then creates the context for the provider named.
        /// </summary>
        /// <remarks>
        /// SQLite is the only provider. There used to be a registry of externally
        /// supplied providers here, reachable through RegisterProvider and filled by MEF
        /// part discovery; neither was ever used, and both are gone.
        /// </remarks>
        private static IContext CreateProvider(IDictionary<string, object> properties)
        {
            ValidateUri(properties);

            var providerName = PropertyResolver.RequireProviderName(properties);

            if (string.Equals(providerName, Config.ProviderSQLite, StringComparison.Ordinal))
            {
                return new Context(properties);
            }

            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, "Unknown provider '{0}'", providerName), nameof(properties));
        }

        /// <summary>
        /// Rejects a uri the named provider cannot work with.
        /// </summary>
        private static void ValidateUri(IDictionary<string, object> properties)
        {
            if (properties == null || !properties.ContainsKey(Config.Uri))
            {
                return;
            }

            var uriName = properties[Config.Uri] as string;
            if (string.IsNullOrEmpty(uriName))
            {
                throw new ArgumentException("Invalid uri property", nameof(properties));
            }

            var providerName = PropertyResolver.RequireProviderName(properties);
            if (!string.Equals(providerName, Config.ProviderSQLite, StringComparison.Ordinal))
            {
                return;
            }

            Uri uri;
            if (!Uri.TryCreate(uriName, UriKind.Absolute, out uri))
            {
                throw new ArgumentException("Invalid uri property", nameof(properties));
            }

            if (!uri.IsFile && !properties.ContainsKey(Config.SQLiteFilename))
            {
                throw new ArgumentException(
                    "Invalid uri scheme, file:///[drive][/path/]filename required", nameof(properties));
            }
        }

        /// <summary>
        /// Resolves the connection string and the caller's properties into one set.
        /// </summary>
        private static IDictionary<string, object> MergeProperties(
            string connectionString,
            IDictionary<string, object> properties)
        {
            try
            {
                return PropertyResolver.Resolve(connectionString, properties);
            }
            catch (HyperliteException e)
            {
                // ht4n reported a bad configuration as an ArgumentException, whatever the
                // provider raised underneath
                throw new ArgumentException(e.Message, e);
            }
        }

        private static string Attribute<T>(Assembly assembly, Func<T, string> select)
            where T : Attribute
        {
            var attributes = assembly.GetCustomAttributes(typeof(T), false);
            return attributes.Length > 0 ? select((T)attributes[0]) : string.Empty;
        }
    }
}
