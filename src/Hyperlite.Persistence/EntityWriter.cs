namespace Hyperlite.Persistence
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Hyperlite.Persistence.Collections;
    using Hyperlite.Persistence.Scanner;
    using Hyperlite.Persistence.Serialization;

    internal sealed class EntityWriter
    {
        private readonly Behaviors behaviors;

        /// <summary>
        ///     Keeps track of the entities written.
        /// </summary>
        private readonly ISet<object> entitiesWritten;

        private readonly EntityContext entityContext;

        private EntityReference entityReference;

        /// <summary>
        ///     The entity key.
        /// </summary>
        private Key key;

        /// <summary>
        ///     Indicating whether entity is a new entity or not.
        /// </summary>
        private bool newEntity;

        private ITableMutator tableMutator;

        private Type tableMutatorType;

        private readonly ISet<Key> ignoreKeys;

        Action<object, Key> reviewKey;

        /// <param name="ignoreKeys">
        ///     The keys to ignore.
        /// </param>
        /// <exception cref="ArgumentException">
        ///     If <see cref="Behaviors.DoNotCache" /> has been combined with <see cref="Behaviors.CreateLazy" />.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     If <see cref="Behaviors.BypassWriteCache" /> has been combined with <see cref="Behaviors.CreateLazy" />.
        /// </exception>
        private EntityWriter(EntityContext entityContext, ISet<Key> ignoreKeys, Behaviors behaviors,
            ISet<object> entitiesWritten)
        {
            if (behaviors.IsCreateLazy())
            {
                if (behaviors.DoNotCache())
                {
                    throw new ArgumentException(@"DoNotCache cannot be combined with CreateLazy", nameof(behaviors));
                }

                if (behaviors.BypassWriteCache())
                {
                    throw new ArgumentException(@"BypassWriteCache cannot be combined with CreateLazy", nameof(behaviors));
                }
            }

            this.entityContext = entityContext;
            this.ignoreKeys = ignoreKeys;
            this.behaviors = behaviors;
            this.entitiesWritten = entitiesWritten;
            this.reviewKey = entityContext.Configuration.ReviewKey;
        }

        /// <summary>
        ///     Persist the given entity using the behavior specified.
        /// </summary>
        /// <returns>
        ///     The entity key.
        /// </returns>
        private static Key Persist(EntityContext entityContext, Type entityType, object entity, Behaviors behaviors,
            ISet<object> entitiesWritten)
        {
            var entityWriter = new EntityWriter(entityContext, null, behaviors, entitiesWritten);
            return entityWriter.Persist(entityType, entity);
        }

        /// <summary>
        ///     Persist the given entity using the behavior specified.
        /// </summary>
        /// <param name="entity">
        ///     The entity to persist.
        /// </param>
        /// <param name="ignoreKeys">
        ///     The keys to ignore.
        /// </param>
        /// <typeparam name="T">
        ///     The entity type.
        /// </typeparam>
        internal static void Persist<T>(EntityContext entityContext, T entity, ISet<Key> ignoreKeys, Behaviors behaviors) where T : class
        {
            var entitiesWritten = behaviors.WriteOnce() ? entityContext.EntitiesWritten : new IdentitySet();
            Persist(entityContext, typeof(T), entity, ignoreKeys, behaviors, entitiesWritten, true, true);
        }

        /// <summary>
        ///     Persist the given entity using the behavior specified.
        /// </summary>
        /// <param name="ignoreKeys">
        ///     The keys to ignore.
        /// </param>
        /// <param name="write">
        ///    if <c>true</c> writes the entity, otherwise only traversing.
        /// </param>
        /// <param name="isRoot">
        ///    if <c>true</c> if entity is the root, otherwise <c>false</c>.
        /// </param>
        /// <returns>
        ///     The entity key.
        /// </returns>
        private static Key Persist(EntityContext entityContext, Type entityType, object entity, ISet<Key> ignoreKeys, Behaviors behaviors,
            ISet<object> entitiesWritten, bool write = true, bool isRoot = false)
        {
            var entityWriter = new EntityWriter(entityContext, ignoreKeys, behaviors, entitiesWritten);
            return entityWriter.Persist(entityType, entity, write, isRoot);
        }

        /// <summary>
        ///     Persist the entity.
        /// </summary>
        /// <param name="write">
        ///    if <c>true</c> writes the entity, otherwise only traversing.
        /// </param>
        /// <param name="isRoot">
        ///    if <c>true</c> if entity is the root, otherwise <c>false</c>.
        /// </param>
        /// <returns>
        ///     The entity key.
        /// </returns>
        /// <exception cref="PersistenceException">
        ///     If entity is not recognized as a valid entity.
        /// </exception>
        private Key Persist(Type entityType, object entity, bool write = true, bool isRoot = false)
        {
            if (this.entitiesWritten.Add(entity))
            {
                // While writing, the serialized bytes stay in the writers unmanaged buffer and go
                // to the mutator straight from there. ToArray would copy them into a managed array
                // just to have them copied again by the mutator.
                using (var buffer = write
                    ? EntitySerializer.SerializeToBuffer(this.entityContext, entityType, entity, this.SerializingEntity)
                    : null)
                {
                    if (!write)
                    {
                        EntitySerializer.Serialize(this.entityContext, entityType, entity,
                            SerializationBase.DefaultCapacity, false, this.SerializingEntity);
                    }

                    if (this.entityReference == null)
                    {
                        throw new PersistenceException(string.Format(CultureInfo.InvariantCulture,
                            @"{0} is not a valid entity", entityType));
                    }

                    write = write && (this.newEntity || !this.behaviors.WriteNewOnly());

                    var dontCache = this.behaviors.DoNotCache();
                    var bypassEntitySpecsFetched =
                        this.newEntity || this.behaviors.IsCreateNew() || this.behaviors.BypassReadCache();
                    var entitySpec = !dontCache || !bypassEntitySpecsFetched
                        ? new EntitySpec(this.entityReference, new Key(this.key))
                        : null;

                    if (dontCache || this.entityContext.EntitySpecsWritten.Add(entitySpec) || (isRoot && this.behaviors.IsCreateNew()) || this.behaviors.BypassWriteCache())
                    {
                        if (write)
                        {
                            if (bypassEntitySpecsFetched || !this.entityContext.EntitySpecsFetched.Contains(entitySpec))
                            {
                                var type = this.entityReference.EntityType;
                                if (this.tableMutatorType != type)
                                {
                                    this.tableMutatorType = type;
                                    this.tableMutator = this.entityContext.GetTableMutator(this.entityReference.Namespace,
                                        this.entityReference.TableName);
                                }

                                //// TODO verbosity?
                                //// Logging.TraceEvent(TraceEventType.Verbose, () => string.Format(CultureInfo.InvariantCulture, @"Set {0}@{1}", this.tableMutator.Key, this.key));
                                this.tableMutator.Set(this.key, buffer.Buffer, buffer.Length);
                            }
                        }
                    }
                }
            }

            return this.key;
        }

        /// <param name="isRoot">
        ///     Indicating whether the entity is the root entity.
        /// </param>
        /// <param name="e">
        ///     The entity.
        /// </param>
        private Key SerializingEntity(bool isRoot, EntityReference er, Type serializeType, object e)
        {
            var write = true;

            if (isRoot)
            {
                this.entityReference = er;

                switch (this.behaviors & Behaviors.CreateBehaviors)
                {
                    case Behaviors.CreateAlways:
                        this.key = er.GenerateKey(e);
                        this.newEntity = true;
                        break;

                    case Behaviors.CreateLazy:
                        this.key = er.GetKeyFromEntity(e, out this.newEntity);
                        if (!this.newEntity)
                        {
                            var entitySpec = new EntitySpec(this.entityReference, new Key(this.key));

                            ////TODO needs to check if the entity has been modified (write,write again)
                            if (!this.entityContext.EntitySpecsWritten.Contains(entitySpec))
                            {
                                ////TODO needs to check if the entity has been modified (read, write)
                                if (this.behaviors.BypassReadCache() || !this.entityContext.EntitySpecsFetched.Contains(entitySpec))
                                {
                                    // given a key of its own here, it is a new entity, as under
                                    // CreateAlways - WriteNewOnly included
                                    this.key = er.GenerateKey(e);
                                    this.newEntity = true;
                                }
                            }
                            else
                            {
                                return null; // cancel further serialization
                            }
                        }

                        break;

                    case Behaviors.CreateNew:
                        this.key = er.GetKeyFromEntity(e, out this.newEntity);
                        break;
                }

                if (this.reviewKey != null)
                {
                    this.reviewKey(e, this.key);
                }

                return this.key;
            }
            else if (this.ignoreKeys != null && (this.behaviors & Behaviors.CreateBehaviors) != Behaviors.CreateAlways)
            {
                var entityKey = er.KeyBinding.KeyFromEntity(e);
                if (entityKey != null && this.ignoreKeys.Contains(entityKey))
                {
                    write = false;
                }
            }

            return Persist(this.entityContext, serializeType, e, this.ignoreKeys, this.behaviors, this.entitiesWritten, write);
        }
    }
}
