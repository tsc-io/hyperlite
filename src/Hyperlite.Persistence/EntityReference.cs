namespace Hyperlite.Persistence
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Globalization;
    using Hyperlite.Persistence.Bindings;

    internal sealed class EntityReference
    {
        /// <summary>
        ///     Might be null.
        /// </summary>
        private readonly IColumnBinding columnBinding;

        private readonly Type entityType;

        private readonly IKeyBinding keyBinding;

        private readonly ITableBinding tableBinding;


        private readonly object syncRoot = new object();

        /// <summary>
        ///     The column set and the column family set, published together.
        /// </summary>
        /// <remarks>
        ///     One immutable holder rather than two fields, because the two are established
        ///     together and have to be seen together: two fields assigned one after the other
        ///     can be seen half set by another thread. Published once, by CompareExchange.
        /// </remarks>
        private ColumnSets columnSets;

        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="entityType" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="tableBinding" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="keyBinding" /> is null.
        /// </exception>
        internal EntityReference(Type entityType, ITableBinding tableBinding, IColumnBinding columnBinding,
            IKeyBinding keyBinding)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            if (tableBinding == null)
            {
                throw new ArgumentNullException(nameof(tableBinding));
            }

            if (keyBinding == null)
            {
                throw new ArgumentNullException(nameof(keyBinding));
            }

            // an entity type is declared, so strict types read it - and what it declares
            Serialization.Encoder.Configuration.TypeAllowList.Declare(entityType);

            this.entityType = entityType;
            this.tableBinding = tableBinding;
            this.columnBinding = columnBinding;
            this.keyBinding = keyBinding;
        }

        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="entityReference" /> is null.
        /// </exception>
        internal EntityReference(EntityReference entityReference)
        {
            if (entityReference == null)
            {
                throw new ArgumentNullException(nameof(entityReference));
            }

            this.entityType = entityReference.entityType;
            this.tableBinding = entityReference.tableBinding;
            this.columnBinding = entityReference.columnBinding;
            this.keyBinding = entityReference.keyBinding;
        }

        internal IColumnBinding ColumnBinding => this.columnBinding;

        internal ISet<string> ColumnFamilySet => Volatile.Read(ref this.columnSets)?.Families;

        internal ISet<string> ColumnSet => Volatile.Read(ref this.columnSets)?.Columns;

        internal Type EntityType => this.entityType;

        internal IKeyBinding KeyBinding => this.keyBinding;

        internal string Namespace => this.tableBinding.Namespace;

        internal ITableBinding TableBinding => this.tableBinding;

        internal string TableName => this.tableBinding.TableName;

        /// <summary>
        ///     Establish the column set for this entity reference.
        /// </summary>
        /// <param name="establishColumns">
        ///     The establish columns function.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If the <paramref name="establishColumns" /> is null.
        /// </exception>
        internal void EstablishColumnSets(Func<IEnumerable<string>> establishColumns)
        {
            if (establishColumns == null)
            {
                throw new ArgumentNullException(nameof(establishColumns));
            }

            if (Volatile.Read(ref this.columnSets) != null)
            {
                return;
            }

            // computed outside any lock, which is safe because it is pure: two threads that
            // both get here compute the same sets, and the first to publish is the one both
            // go on to use
            ISet<string> columnFamilies;
            var columns = ScanSpec.DistinctColumn(establishColumns(), out columnFamilies);
            Interlocked.CompareExchange(ref this.columnSets, new ColumnSets(columns, columnFamilies), null);
        }

        /// <summary>
        ///     Generates a valid database key for the entity specified.
        /// </summary>
        /// <returns>
        ///     A valid database key.
        /// </returns>
        internal Key GenerateKey(object entity)
        {
            lock (this.syncRoot)
            {
                return this.VerifyKey(this.keyBinding.CreateKey(entity), true);
            }
        }

        /// <summary>
        ///     The key the entity has, or null when it has none yet - never one created for it,
        ///     and not verified: one the caller has made invalid since is simply none.
        /// </summary>
        internal Key KeyOfEntity(object entity)
        {
            lock (this.syncRoot)
            {
                var key = this.keyBinding.KeyFromEntity(entity);
                return key == null || string.IsNullOrEmpty(key.Row) ? null : key;
            }
        }

        /// <summary>
        ///     Gets or creates a database key from the entity specified.
        /// </summary>
        /// <param name="generated">
        ///     <c>true</c> if the database key has been generated, otherwise <c>false</c>.
        /// </param>
        /// <returns>
        ///     A valid database key.
        /// </returns>
        internal Key GetKeyFromEntity(object entity, out bool generated)
        {
            lock (this.syncRoot)
            {
                var key = this.keyBinding.KeyFromEntity(entity);
                generated = key == null || string.IsNullOrEmpty(key.Row);
                return this.VerifyKey(generated ? this.keyBinding.CreateKey(entity) : key, true);
            }
        }

        /// <summary>
        ///     Gets a database key from the object specified.
        /// </summary>
        /// <returns>
        ///     A valid database key.
        /// </returns>
        /// <exception cref="PersistenceException">
        ///     If it's not possible to derive a valid key from <paramref name="entityOrKey" />.
        /// </exception>
        internal Key GetKeyFromObject(object entityOrKey, bool verifyColumnFamily)
        {
            lock (this.syncRoot)
            {
                Key entityKey;
                if (this.EntityType.IsInstanceOfType(entityOrKey))
                {
                    entityKey = this.keyBinding.KeyFromEntity(entityOrKey);
                    if (entityKey == null)
                    {
                        throw new PersistenceException(string.Format(CultureInfo.InvariantCulture,
                            @"Query key from entity {0} failed", entityOrKey));
                    }
                }
                else
                {
                    entityKey = this.keyBinding.KeyFromValue(entityOrKey);
                    if (entityKey == null)
                    {
                        throw new PersistenceException(string.Format(CultureInfo.InvariantCulture,
                            @"Query key from value {0} failed", entityOrKey));
                    }
                }

                return this.VerifyKey(entityKey, verifyColumnFamily);
            }
        }

        /// <summary>
        ///     Updates the entity using the database key specified.
        /// </summary>
        /// <param name="key">
        ///     The database key.
        /// </param>
        internal void SetKey(object entity, Key key)
        {
            lock (this.syncRoot)
            {
                this.keyBinding.SetKey(entity, this.VerifyKey(key, true));
            }
        }

        /// <summary>
        ///     Validates the specified database key.
        /// </summary>
        /// <param name="key">
        ///     The database key to validate.
        /// </param>
        /// <returns>
        ///     The validated database key.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="key" /> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     If there is no row set for <paramref name="key" />.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     If there is no column family set for <paramref name="key" />.
        /// </exception>
        private Key VerifyKey(Key key, bool verifyColumnFamily)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (string.IsNullOrEmpty(key.Row))
            {
                throw new ArgumentException(@"Row key has not been specified", nameof(key));
            }

            if (verifyColumnFamily && string.IsNullOrEmpty(key.ColumnFamily))
            {
                var partialKeyBinding = this.keyBinding as PartialKeyBinding;
                if (partialKeyBinding != null && partialKeyBinding.ColumnBinding == null)
                {
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        @"Undefined column binding for type {0}", this.entityType));
                }

                throw new ArgumentException(@"Missing column family)", nameof(key));
            }

            return key;
        }

        /// <summary>
        ///     The two column sets of an entity reference, which are only ever seen together.
        /// </summary>
        private sealed class ColumnSets
        {
            internal readonly ISet<string> Columns;

            internal readonly ISet<string> Families;

            internal ColumnSets(ISet<string> columns, ISet<string> families)
            {
                this.Columns = columns;
                this.Families = families;
            }
        }
    }
}
