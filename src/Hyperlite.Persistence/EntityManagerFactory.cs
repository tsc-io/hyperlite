namespace Hyperlite.Persistence
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Reflection;
    using Hyperlite.Persistence.Extensions;

    public sealed class EntityManagerFactory : IDisposable
    {
        private static readonly PersistenceConfiguration DefaultConfigurationInstance = new PersistenceConfiguration();

        /// <summary>
        ///     The entity manager factory context.
        /// </summary>
        private readonly FactoryContext factoryContext;

        /// <summary>
        ///     Indicating whether this factory context has been disposed.
        /// </summary>
        private bool disposed;

        /// <summary>
        ///     Initializes static members of the <see cref="EntityManagerFactory" /> class.
        /// </summary>
        static EntityManagerFactory()
        {
            Logging.TraceEvent(
                TraceEventType.Information,
                () =>
                {
                    var assembly = Assembly.GetAssembly(typeof(EntityManagerFactory));
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        @"{0} v{1} ({2}, {3})",
                        assembly.GetName().Name,
                        GetAssemblyAttribute<AssemblyFileVersionAttribute>().Version,
                        GetAssemblyAttribute<AssemblyCompanyAttribute>().Company,
                        GetAssemblyAttribute<AssemblyCopyrightAttribute>().Copyright);
                });
        }

        /// <param name="disposeContext">
        ///     Indicating whether this entity manager factory owns the database context.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="configuration" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="context" /> is null.
        /// </exception>
        private EntityManagerFactory(PersistenceConfiguration configuration, IContext context,
            bool disposeContext = true)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            this.factoryContext = new FactoryContext(configuration, context, disposeContext);
        }

        public static PersistenceConfiguration DefaultConfiguration => DefaultConfigurationInstance;

        /// <summary>
        ///     Do not dispose the returned database client, it's owned by the factory context.
        /// </summary>
        /// <value>
        ///     The database client.
        /// </value>
        public IClient Client
        {
            get
            {
                this.ThrowIfDisposed();
                return this.factoryContext.Client;
            }
        }

        public PersistenceConfiguration Configuration => this.factoryContext.Configuration;

        /// <summary>
        ///     Gets the database context properties.
        /// </summary>
        /// <value>
        ///     The database context properties.
        /// </value>
        public IDictionary<string, object> Properties
        {
            get
            {
                this.ThrowIfDisposed();
                return this.factoryContext.Properties;
            }
        }

        /// <value>
        ///     The database root namespace.
        /// </value>
        public INamespace RootNamespace
        {
            get
            {
                this.ThrowIfDisposed();
                return this.factoryContext.RootNamespace;
            }
        }

        /// <summary>
        ///     Create a new entity manager factory using the configuration properties specified.
        /// </summary>
        /// <param name="properties">
        ///     The configuration properties.
        /// </param>
        /// <returns>
        ///     The newly created entity manager factory.
        /// </returns>
        public static EntityManagerFactory CreateEntityManagerFactory(IDictionary<string, object> properties)
        {
            return CreateEntityManagerFactory(Context.Create(properties), true, DefaultConfigurationInstance);
        }

        /// <summary>
        ///     Create a new entity manager factory using the connection string specified.
        /// </summary>
        /// <returns>
        ///     The newly created entity manager factory.
        /// </returns>
        public static EntityManagerFactory CreateEntityManagerFactory(string connectionString)
        {
            return CreateEntityManagerFactory(Context.Create(connectionString), true, DefaultConfigurationInstance);
        }

        /// <summary>
        ///     Create a new entity manager factory using the connection string and the configuration properties specified.
        /// </summary>
        /// <param name="properties">
        ///     The configuration properties.
        /// </param>
        /// <returns>
        ///     The newly created entity manager factory.
        /// </returns>
        public static EntityManagerFactory CreateEntityManagerFactory(string connectionString,
            IDictionary<string, object> properties)
        {
            return CreateEntityManagerFactory(Context.Create(connectionString, properties), true,
                DefaultConfigurationInstance);
        }

        /// <summary>
        ///     Create a new entity manager factory using the database context specified.
        /// </summary>
        /// <param name="ctx">
        ///     The existing database context.
        /// </param>
        /// <returns>
        ///     The newly created entity manager factory.
        /// </returns>
        /// <remarks>
        ///     The entity manager does not take ownership of the database context specified.
        /// </remarks>
        public static EntityManagerFactory CreateEntityManagerFactory(IContext ctx)
        {
            return CreateEntityManagerFactory(ctx, false, DefaultConfigurationInstance);
        }

        /// <summary>
        ///     Create a new entity manager factory using the configuration properties and the persistence configuration specified.
        /// </summary>
        /// <param name="properties">
        ///     The configuration properties.
        /// </param>
        /// <returns>
        ///     The newly created entity manager factory.
        /// </returns>
        public static EntityManagerFactory CreateEntityManagerFactory(IDictionary<string, object> properties,
            PersistenceConfiguration configuration)
        {
            return CreateEntityManagerFactory(Context.Create(properties), true, configuration);
        }

        /// <summary>
        ///     Create a new entity manager factory using the connection string specified and the persistence configuration
        ///     specified.
        /// </summary>
        /// <returns>
        ///     The newly created entity manager factory.
        /// </returns>
        public static EntityManagerFactory CreateEntityManagerFactory(string connectionString,
            PersistenceConfiguration configuration)
        {
            return CreateEntityManagerFactory(Context.Create(connectionString), true, configuration);
        }

        /// <summary>
        ///     Create a new entity manager factory using the connection string, the configuration properties and the persistence
        ///     configuration specified.
        /// </summary>
        /// <param name="properties">
        ///     The configuration properties.
        /// </param>
        /// <returns>
        ///     The newly created entity manager factory.
        /// </returns>
        public static EntityManagerFactory CreateEntityManagerFactory(string connectionString,
            IDictionary<string, object> properties, PersistenceConfiguration configuration)
        {
            return CreateEntityManagerFactory(Context.Create(connectionString, properties), true, configuration);
        }

        /// <summary>
        ///     Create a new entity manager factory using the database context and the persistence configuration specified.
        /// </summary>
        /// <param name="ctx">
        ///     The existing database context.
        /// </param>
        /// <returns>
        ///     The newly created entity manager factory.
        /// </returns>
        /// <remarks>
        ///     The entity manager does not take ownership of the database context specified.
        /// </remarks>
        public static EntityManagerFactory CreateEntityManagerFactory(IContext ctx,
            PersistenceConfiguration configuration)
        {
            return CreateEntityManagerFactory(ctx, false, configuration);
        }

        /// <summary>
        ///     Clears the entire entity manager factory context.
        /// </summary>
        public void Clear()
        {
            this.ThrowIfDisposed();
            this.factoryContext.Clear();
        }

        /// <summary>
        ///     Create a new entity manager using the default binding context.
        /// </summary>
        /// <returns>
        ///     The newly created entity manager.
        /// </returns>
        public EntityManager CreateEntityManager()
        {
            this.ThrowIfDisposed();
            return new EntityManager(this.factoryContext);
        }

        /// <summary>
        ///     Create a new entity manager using the binding context specified.
        /// </summary>
        /// <returns>
        ///     The newly created entity manager.
        /// </returns>
        public EntityManager CreateEntityManager(BindingContext bindingContext)
        {
            this.ThrowIfDisposed();
            return new EntityManager(this.factoryContext, bindingContext);
        }

        public void Dispose()
        {
            this.Dispose(true);
        }

        /// <summary>
        ///     Create a new entity manager factory using the database context and the persistence configuration specified.
        /// </summary>
        /// <param name="ctx">
        ///     The existing database context.
        /// </param>
        /// <param name="disposeContext">
        ///     Indicating whether this factory context owns the database context.
        /// </param>
        /// <returns>
        ///     The newly created entity manager factory.
        /// </returns>
        /// <remarks>
        ///     The entity manager does not take ownership of the database context specified.
        /// </remarks>
        private static EntityManagerFactory CreateEntityManagerFactory(IContext ctx, bool disposeContext,
            PersistenceConfiguration configuration)
        {
            return new EntityManagerFactory(
                DefaultConfigurationInstance != configuration
                    ? configuration
                    : new PersistenceConfiguration(DefaultConfigurationInstance), ctx, disposeContext);
        }

        /// <summary>
        ///     Gets an assembly attribute.
        /// </summary>
        /// <typeparam name="T">
        ///     The type of the assembly attribute to query.
        /// </typeparam>
        /// <returns>
        ///     The assembly attribute of type T or null otherwise.
        /// </returns>
        private static T GetAssemblyAttribute<T>() where T : Attribute
        {
            return Assembly.GetAssembly(typeof(EntityManagerFactory)).GetAttribute<T>();
        }

        private void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    this.factoryContext.Dispose();
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
                throw new ObjectDisposedException("Entity manager factory has been already disposed");
            }
        }
    }
}
