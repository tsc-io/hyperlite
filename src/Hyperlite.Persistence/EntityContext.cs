namespace Hyperlite.Persistence
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.ExceptionServices;
    using System.Threading;
    using EntitySpecSet =
        Hyperlite.Persistence.Collections.Concurrent.SynchronizedSet<Hyperlite.Persistence.Scanner.EntitySpec>;
    using TableMutatorDictionary =
        Hyperlite.Persistence.Collections.Concurrent.ConcurrentStringDictionary<ITableMutator>;
    using SynchronizedIdentitySet = Hyperlite.Persistence.Collections.Concurrent.SynchronizedIdentitySet;

    /// <summary>
    ///     The entity context: the state an entity manager keeps between calls.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Safe to use from several threads at once, because the manager it belongs to
    ///         is. Everything here that more than one call writes is concurrent: the three
    ///         sets that record what has been fetched and written, and the table mutators,
    ///         of which there is one per table shared by every thread - which is safe
    ///         because both kinds this creates lock every entry point. See
    ///         TestEntityManagerConcurrency, which is what established that this had not
    ///         been true.
    ///     </para>
    ///     <para>
    ///         Disposal is idempotent and may happen on any thread. A call made after it fails
    ///         with an <see cref="ObjectDisposedException" />; one running concurrently with it
    ///         either completes or fails with one. Nothing is nulled, so it never fails with
    ///         anything worse.
    ///     </para>
    /// </remarks>
    internal sealed class EntityContext : EntityBindingContext, IDisposable
    {
        private readonly PersistenceConfiguration configuration;

        /// <summary>
        ///     The entity manager factory context.
        /// </summary>
        private readonly FactoryContext factoryContext;

        private readonly TableMutatorDictionary tableMutators = new TableMutatorDictionary();

        /// <summary>
        ///     Nonzero once disposal has begun. An int so that it can be claimed once with
        ///     <see cref="Interlocked.Exchange(ref int, int)" />.
        /// </summary>
        private int disposed;

        /// <summary>
        ///     The mutator specification adjusted to the database context, see EffectiveMutatorSpec.
        /// </summary>
        private volatile MutatorSpec effectiveMutatorSpec;

        private readonly EntitySpecSet entitySpecsFetched = new EntitySpecSet();

        private readonly EntitySpecSet entitySpecsWritten = new EntitySpecSet();

        /// <summary>
        ///     Keeps track of the entities written.
        /// </summary>
        private readonly ISet<object> entitiesWritten = new SynchronizedIdentitySet();

        internal EntityContext(FactoryContext factoryContext, BindingContext bindingContext)
            : base(bindingContext)
        {
            this.factoryContext = factoryContext;
            this.configuration = new PersistenceConfiguration(factoryContext.Configuration, this);
        }

        /// <summary>
        ///     Do not dispose the returned database client, it's owned by the entity context.
        /// </summary>
        /// <value>
        ///     The database client.
        /// </value>
        internal IClient Client
        {
            get
            {
                this.ThrowIfDisposed();
                return this.factoryContext.Client;
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

        internal EntitySpecSet EntitySpecsFetched => this.entitySpecsFetched;

        internal EntitySpecSet EntitySpecsWritten => this.entitySpecsWritten;

        internal ISet<object> EntitiesWritten => this.entitiesWritten;

        public ISet<string> ColumnFamiliesForType(Type entityType)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            entityReference.EstablishColumnSets(() => this.ColumnNames(entityReference));
            return entityReference.ColumnFamilySet;
        }

        public void Dispose()
        {
            // claimed once, whichever thread gets here first
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            ITableMutator[] mutators;
            lock (this.factoryContext.SyncRoot)
            {
                mutators = this.tableMutators.Values.ToArray();
                this.tableMutators.Clear();
            }

            // outside the factory lock, because disposing a mutator flushes it. A flush that
            // fails throws; the others are still disposed, then the first failure is thrown
            ExceptionDispatchInfo failure = null;
            foreach (var tableMutator in mutators)
            {
                try
                {
                    tableMutator.Dispose();
                }
                catch (Exception e)
                {
                    failure ??= ExceptionDispatchInfo.Capture(e);
                }
            }

            // cleared, not nulled: a call still running sees empty sets and then an
            // ObjectDisposedException, never a null
            this.entitySpecsFetched.Clear();
            this.entitySpecsWritten.Clear();
            this.entitiesWritten.Clear();

            failure?.Throw();
        }

        /// <summary>
        ///     Fetches all entities of the entity type specified from the database.
        /// </summary>
        /// <returns>
        ///     The entities fetched.
        /// </returns>
        public IEnumerable Fetch(Type entityType, Behaviors behaviors)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            var scanSpec = this.ScanSpecForType(entityType);
            return EntityReader.Read(this, entityReference, scanSpec, behaviors);
        }

        /// <summary>
        ///     Fetches all entities of the entity type specified from the database.
        /// </summary>
        /// <param name="entitySink">
        ///     The entity sink, receives the entities fetched.
        /// </param>
        public void Fetch(Type entityType, Action<object> entitySink, Behaviors behaviors)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            var scanSpec = this.ScanSpecForType(entityType);
            EntityReader.Read(this, entityReference, scanSpec, entitySink, behaviors);
        }

        /// <summary>
        ///     Fetches all entities of the types specified from the database.
        /// </summary>
        /// <returns>
        ///     The entities fetched.
        /// </returns>
        public IEnumerable Fetch(Type entityType, IEnumerable<Type> queryTypes, Behaviors behaviors)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            return EntityReader.Read(this, entityReference, this.ScanSpecForType(queryTypes), behaviors);
        }

        /// <summary>
        ///     Fetches all entities of the types specified from the database.
        /// </summary>
        /// <param name="entitySink">
        ///     The entity sink, receives the entities fetched.
        /// </param>
        public void Fetch(Type entityType, IEnumerable<Type> queryTypes, Action<object> entitySink, Behaviors behaviors)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            EntityReader.Read(this, entityReference, this.ScanSpecForType(queryTypes), entitySink, behaviors);
        }

        /// <summary>
        ///     Fetches all entities using the given scan specification from the database.
        /// </summary>
        /// <param name="scanSpec">
        ///     The scan specification.
        /// </param>
        /// <returns>
        ///     The entities fetched.
        /// </returns>
        public IEnumerable Fetch(Type entityType, ScanSpec scanSpec, Behaviors behaviors)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            if (scanSpec == null)
            {
                throw new ArgumentNullException(nameof(scanSpec));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            return EntityReader.Read(this, entityReference, scanSpec, behaviors);
        }

        /// <summary>
        ///     Fetches all entities using the given scan specification from the database.
        /// </summary>
        /// <param name="scanSpec">
        ///     The scan specification.
        /// </param>
        /// <param name="entitySink">
        ///     The entity sink, receives the entities fetched.
        /// </param>
        public void Fetch(Type entityType, ScanSpec scanSpec, Action<object> entitySink, Behaviors behaviors)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            if (scanSpec == null)
            {
                throw new ArgumentNullException(nameof(scanSpec));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            EntityReader.Read(this, entityReference, scanSpec, entitySink, behaviors);
        }

        /// <summary>
        ///     Find an entity in the database using the key provider specified.
        /// </summary>
        /// <returns>
        ///     The found entity instance or null if the entity does not exist.
        /// </returns>
        public object Find(Type entityType, object keyProvider, Behaviors behaviors)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            if (keyProvider == null)
            {
                throw new ArgumentNullException(nameof(keyProvider));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            return EntityReader.Read(this, entityReference, keyProvider, behaviors);
        }

        /// <summary>
        ///     Find the entities in the database using the key providers specified.
        /// </summary>
        /// <returns>
        ///     The entities found.
        /// </returns>
        public IEnumerable FindMany(Type entityType, IEnumerable keyProviders, Behaviors behaviors)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            if (keyProviders == null)
            {
                throw new ArgumentNullException(nameof(keyProviders));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            return EntityReader.Read(this, entityReference, keyProviders, behaviors);
        }

        /// <summary>
        ///     Find the entities in the database using the key providers specified.
        /// </summary>
        /// <param name="entitySink">
        ///     The entity sink, receives the entities fetched.
        /// </param>
        public void FindMany(Type entityType, IEnumerable keyProviders, Action<object> entitySink, Behaviors behaviors)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            if (keyProviders == null)
            {
                throw new ArgumentNullException(nameof(keyProviders));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            EntityReader.Read(this, entityReference, keyProviders, entitySink, behaviors);
        }

        /// <summary>
        ///     Gets the database namespace instance for the entity type specified.
        /// </summary>
        /// <returns>
        ///     The database namespace instance.
        /// </returns>
        /// <remarks>
        ///     Do not dispose the returned namespace instance, it's owned by the entity context.
        /// </remarks>
        public INamespace GetNamespace(Type entityType)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            return this.factoryContext.GetNamespace(entityReference.Namespace);
        }

        /// <summary>
        ///     Gets the database table instance for the entity type specified.
        /// </summary>
        /// <returns>
        ///     The database table instance.
        /// </returns>
        /// <remarks>
        ///     Do not dispose the returned table instance, it's owned by the entity context.
        /// </remarks>
        public ITable GetTable(Type entityType)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            return this.GetTable(entityReference.Namespace, entityReference.TableName);
        }

        /// <summary>
        ///     Merge the state of the given entity into the database.
        /// </summary>
        /// <param name="entity">
        ///     The entity to merge.
        /// </param>
        /// <typeparam name="T">
        ///     The entity type.
        /// </typeparam>
        public void Merge<T>(T entity) where T : class
        {
            ////TODO implement real merge
            this.Persist(entity, null, Behaviors.CreateNew);
        }

        /// <summary>
        ///     Stores an entity instance to the database.
        /// </summary>
        /// <param name="entity">
        ///     The entity to store.
        /// </param>
        /// <param name="ignoreKeys">
        ///     The keys to ignore.
        /// </param>
        /// <typeparam name="T">
        ///     The entity type.
        /// </typeparam>
        public void Persist<T>(T entity, ISet<Key> ignoreKeys, Behaviors behaviors) where T : class
        {
            EntityWriter.Persist(this, entity, ignoreKeys, behaviors);

            if (behaviors.BypassWriteCache())
            {
                this.entitiesWritten.Remove(entity);
            }
        }

        /// <summary>
        ///     Remove an entity instance from the database using the key provider specified.
        /// </summary>
        public void Remove(Type entityType, object keyProvider)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            if (keyProvider == null)
            {
                throw new ArgumentNullException(nameof(keyProvider));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            var key = entityReference.GetKeyFromObject(keyProvider, true);
            this.GetTableMutator(entityReference.Namespace, entityReference.TableName).Delete(key);
            this.Forget(new[] { new Scanner.EntitySpec(entityReference, key) });
        }

        /// <summary>
        ///     Removes all entities using the given scan specification from the database.
        /// </summary>
        /// <param name="scanSpec">
        ///     The scan specification.
        /// </param>
        public void Remove(Type entityType, ScanSpec scanSpec)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            if (scanSpec == null)
            {
                throw new ArgumentNullException(nameof(scanSpec));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            // a copy, so the caller's scan spec is left as it was given
            if (!scanSpec.KeysOnly)
            {
                scanSpec = new ScanSpec(scanSpec) { KeysOnly = true };
            }

            var table = this.GetTable(entityReference.Namespace, entityReference.TableName);
            var mutator = this.GetTableMutator(entityReference.Namespace, entityReference.TableName);
            var removed = new List<Scanner.EntitySpec>();
            using (var scanner = table.CreateScanner(scanSpec))
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    mutator.Delete(cell.Key);
                    removed.Add(new Scanner.EntitySpec(entityReference, new Key(cell.Key)));
                }
            }

            this.Forget(removed);
        }

        /// <summary>
        ///     Drops removed entities from what this context has written and fetched, and from the
        ///     entities written once, see Behaviors.WriteOnce, whichever way they were removed.
        ///     Left there, a later persist of one in this context would be skipped as written
        ///     already, and an entity referring to it would be written with a reference to nothing.
        /// </summary>
        private void Forget(ICollection<Scanner.EntitySpec> removed)
        {
            foreach (var entitySpec in removed)
            {
                this.entitySpecsWritten.Remove(entitySpec);
                this.entitySpecsFetched.Remove(entitySpec);
            }

            // empty unless Behaviors.WriteOnce has been used, and then matched by key: removed by
            // its key or by a scan, an entity is not at hand
            if (removed.Count == 0 || this.entitiesWritten.Count == 0)
            {
                return;
            }

            // matched through each written entity's own reference: a spec compares key, namespace
            // and table, so an entity of another type sharing the table is found as well
            var keys = new HashSet<Scanner.EntitySpec>(removed);
            foreach (var written in this.entitiesWritten)
            {
                var reference = this.EntityReferenceForType(written.GetType());
                var key = reference?.KeyOfEntity(written);
                if (key != null && keys.Contains(new Scanner.EntitySpec(reference, key)))
                {
                    this.entitiesWritten.Remove(written);
                }
            }
        }

        public ScanSpec ScanSpecForType(Type entityType)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            var entityReference = this.EntityReferenceForType(entityType);
            if (entityReference == null)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            entityReference.EstablishColumnSets(() => this.ColumnNames(entityReference));
            var scanSpec = new ScanSpec { MaxVersions = 1 };
            scanSpec.AddColumn(entityReference.ColumnSet);
            if (scanSpec.ColumnCount == 0)
            {
                throw new PersistenceException(string.Format(CultureInfo.InvariantCulture, @"{0} is not a valid entity",
                    entityType));
            }

            return scanSpec;
        }

        /// <summary>
        ///     Gets the merged scan spec for the types specified.
        /// </summary>
        /// <param name="entityTypes">
        ///     The query types.
        /// </param>
        /// <returns>
        ///     The merged scan spec.
        /// </returns>
        public ScanSpec ScanSpecForType(IEnumerable<Type> entityTypes)
        {
            if (entityTypes == null)
            {
                throw new ArgumentNullException(nameof(entityTypes));
            }

            var columnNames = new List<string>();
            foreach (var queryType in entityTypes)
            {
                var entityReference = this.EntityReferenceForType(queryType);
                if (entityReference == null)
                {
                    throw new PersistenceException(string.Format(CultureInfo.InvariantCulture,
                        @"{0} is not a valid query type", queryType));
                }

                entityReference.EstablishColumnSets(() => this.ColumnNames(entityReference));
                columnNames.AddRange(entityReference.ColumnSet);
            }

            if (columnNames.Count > 5)
            {
                var registeredColumnNames = this.RegisteredColumnNames();

                foreach (var columnName in columnNames.ToList())
                {
                    var split = columnName.Split(':');
                    if (split.Length > 1)
                    {
                        ISet<string> columnQualifiers;
                        if (registeredColumnNames.TryGetValue(split[0], out columnQualifiers))
                        {
                            if (columnQualifiers.Remove(split[1]) && columnQualifiers.Count == 0)
                            {
                                columnNames.Add(split[0]);
                            }
                        }
                    }
                }
            }

            var scanSpec = new ScanSpec { MaxVersions = 1 };
            scanSpec.AddColumn(ScanSpec.DistinctColumn(columnNames));
            if (scanSpec.ColumnCount == 0)
            {
                throw new PersistenceException("Missing or invalid entity types");
            }

            return scanSpec;
        }

        /// <summary>
        ///     Flushes all table mutators.
        /// </summary>
        internal void Flush()
        {
            this.ThrowIfDisposed();
            foreach (var tableMutator in this.tableMutators.Values)
            {
                tableMutator.Flush();
            }
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
        ///     Do not dispose the returned table instance, it's owned by the entity context.
        /// </remarks>
        internal ITable GetTable(string ns, string tableName)
        {
            return this.factoryContext.GetTable(ns, tableName);
        }

        /// <summary>
        ///     Gets the table mutator for the namespace/table name specified.
        /// </summary>
        /// <remarks>
        ///     One per table, shared by every thread using this manager. That is safe
        ///     because <see cref="EffectiveMutatorSpec" /> only ever yields a plain or a
        ///     chunked mutator - a queued one is downgraded - and both of those lock every
        ///     entry point. See ITableMutator.
        /// </remarks>
        /// <param name="ns">
        ///     The namespace name.
        /// </param>
        internal ITableMutator GetTableMutator(string ns, string tableName)
        {
            lock (this.factoryContext.SyncRoot)
            {
                // under the lock Dispose takes to collect the mutators, so a mutator is
                // either created before disposal collects it or not created at all
                this.ThrowIfDisposed();
                return this.tableMutators.GetOrAdd(this.GetFullyQualifiedTableName(ns, tableName),
                s => this.GetTable(ns, tableName).CreateMutator(this.EffectiveMutatorSpec));
            }
        }

        /// <summary>
        ///     Gets the mutator specification to use, adjusted to what the database context supports.
        /// </summary>
        /// <remarks>
        ///     Queuing hands the cells to a second thread, which pays off while the database context
        ///     works asynchronously. A context without asynchronous mutators writes synchronously, so
        ///     the queue would only add a thread, a cell plus a cloned key per set and latency before
        ///     the data reaches the database.
        /// </remarks>
        private MutatorSpec EffectiveMutatorSpec
        {
            get
            {
                if (this.effectiveMutatorSpec == null)
                {
                    var spec = this.configuration.MutatorSpec;
                    // a queued mutator needs an asynchronous one underneath, which no
                    // provider here has, so the request is downgraded rather than refused
                    if (spec.Queued)
                    {
                        spec = new MutatorSpec(spec) { Queued = false };
                    }

                    this.effectiveMutatorSpec = spec;
                }

                return this.effectiveMutatorSpec;
            }
        }

        /// <param name="ns">
        ///     The namespace name.
        /// </param>
        private string GetFullyQualifiedTableName(string ns, string tableName)
        {
            return this.factoryContext.GetFullyQualifiedTableName(ns, tableName);
        }

        /// <summary>
        ///     The throw if the object has been disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        ///     If the object has been already disposed.
        /// </exception>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref this.disposed) != 0)
            {
                throw new ObjectDisposedException("Entity context has been already disposed");
            }
        }
    }
}
