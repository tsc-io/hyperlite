namespace Hyperlite.Persistence
{
    using System;
    using System.Collections;
    using System.Linq;
    using Hyperlite;
    using Hyperlite.Persistence.Collections;
    using Hyperlite.Persistence.Scanner;
    using Hyperlite.Persistence.Serialization;
    using EntitySpecDictionary =
        System.Collections.Concurrent.ConcurrentDictionary<Hyperlite.Persistence.Scanner.EntitySpec,
            object>;
    using EntitySpecSet =
        Hyperlite.Persistence.Collections.Concurrent.SynchronizedSet<Hyperlite.Persistence.Scanner.EntitySpec>;

    internal sealed class EntityReader
    {
        private readonly Behaviors behaviors;

        /// <summary>
        ///     Buckets for the fetched-entity map, which is the one collection on this path
        ///     that has to stay concurrent.
        /// </summary>
        /// <remarks>
        ///     The framework's ConcurrentDictionary starts at 31 buckets and takes every one
        ///     of its locks to grow, so a fetch adding thousands of entries rehashes under
        ///     full contention several times. Measured on entity.fetch.wide, leaving it at
        ///     the default cost 16%. Growth past this figure is no worse than it was; what
        ///     the figure buys is the first handful of rehashes, which are the ones that land
        ///     while every task is inserting.
        /// </remarks>
        private const int ScanCapacity = 4096;

        /// <summary>
        ///     The entities fetched, keyed by spec. Written from the deserializing tasks.
        /// </summary>
        /// <remarks>
        ///     Sized by the constructor from what the read expects. It used to be a field
        ///     initialized to <see cref="ScanCapacity" />, so every reader paid for 4,096
        ///     buckets - and a reader is created per Find.
        /// </remarks>
        private readonly EntitySpecDictionary entitiesFetched;

        /// <summary>
        ///     Serializes delivery of a fetched entity to the caller, and the bookkeeping
        ///     that goes with it.
        /// </summary>
        /// <remarks>
        ///     A fetch deserializes on several tasks, and each of them ends by invoking code
        ///     the caller wrote - the sink of <c>Fetch&lt;T&gt;(sink)</c>, or a property
        ///     setter on an owning entity. Calling that concurrently is not something a
        ///     caller can be expected to have allowed for: a sink of <c>list.Add</c> reads
        ///     naturally and loses entities under it, which is how this was found - 147
        ///     arriving out of 150.
        ///     <para>
        ///     Only the delivery is held. Deserialization is why the tasks exist and stays
        ///     outside, so the lock costs one uncontended acquisition per entity against
        ///     work that already involves reading and decoding a cell.
        ///     </para>
        /// </remarks>
        private readonly object deliveryGate = new object();

        private readonly EntityScanner entityScanner;

        private readonly EntitySpecSet entitySpecsFetched;

        /// <param name="expectedEntities">
        ///     How many entities the read is likely to deliver: 1 for a find, the key count
        ///     for a find of several, what the scan spec bounds it to for a scan. Anything up to
        ///     the framework's default size gets the default.
        /// </param>
        private EntityReader(EntityScanner entityScanner, Behaviors behaviors, int expectedEntities)
        {
            this.entityScanner = entityScanner;
            this.behaviors = behaviors;
            this.entitySpecsFetched = entityScanner.EntityContext.EntitySpecsFetched;

            // presized only where a read adds many entries from several tasks at once: the
            // framework grows under every one of its locks, which cost 16% on
            // entity.fetch.wide at the default size
            this.entitiesFetched = expectedEntities > DefaultCapacity
                ? new EntitySpecDictionary(System.Environment.ProcessorCount, expectedEntities)
                : new EntitySpecDictionary();
        }

        /// <summary>
        ///     The framework's own initial size, below which presizing buys nothing.
        /// </summary>
        private const int DefaultCapacity = 31;

        /// <summary>
        ///     How many entities a find of several keys expects: their count, where it is
        ///     known without enumerating them.
        /// </summary>
        private static int ExpectedFor(System.Collections.IEnumerable keys)
        {
            var collection = keys as System.Collections.ICollection;
            return collection != null ? collection.Count : 0;
        }

        /// <summary>
        ///     How many entities a scan expects: <see cref="ScanCapacity" />, unless the scan
        ///     spec bounds the result below it.
        /// </summary>
        /// <remarks>
        ///     Only bounds that are certain count. Explicit cells and nothing else deliver at
        ///     most one entity each. A cell limit applies per row interval and per cell
        ///     interval - the native scanner restarts its count for each - so it bounds the
        ///     scan as the limit times the parts, or once for a scan of the whole table. Rows
        ///     without a limit can hold any number of entities, and keep the full size.
        /// </remarks>
        private static int ExpectedFor(ScanSpec scanSpec)
        {
            if (scanSpec == null)
            {
                return ScanCapacity;
            }

            long bound = ScanCapacity;
            var parts = (long)scanSpec.RowCount + scanSpec.CellCount + scanSpec.RowIntervalCount
                        + scanSpec.CellIntervalCount;
            if (scanSpec.CellCount > 0 && parts == scanSpec.CellCount)
            {
                bound = Math.Min(bound, scanSpec.CellCount);
            }

            if (scanSpec.MaxCells > 0)
            {
                bound = Math.Min(bound, scanSpec.MaxCells * Math.Max(parts, 1));
            }

            return (int)bound;
        }

        internal EntityScanner EntityScanner => this.entityScanner;

        /// <summary>
        ///     Reads an entity which belongs to the given database key from the database.
        /// </summary>
        /// <param name="key">
        ///     The database key.
        /// </param>
        /// <returns>
        ///     The entity.
        /// </returns>
        internal static object Read(EntityContext entityContext, EntityReference entityReference, object key,
            Behaviors behaviors)
        {
            var entityScanTarget = new EntityScanTarget(entityReference, key);
            var entityScanner = new EntityScanner(entityContext);
            entityScanner.Add(entityScanTarget);
            new EntityReader(entityScanner, behaviors, 1).Read();
            return entityScanTarget.Value;
        }

        /// <summary>
        ///     Reads all entities which belongs to the given database keys from the database.
        /// </summary>
        /// <param name="keys">
        ///     The entity keys.
        /// </param>
        /// <returns>
        ///     The entities.
        /// </returns>
        internal static IEnumerable Read(EntityContext entityContext, EntityReference entityReference, IEnumerable keys,
            Behaviors behaviors)
        {
            var entityScanner = new EntityScanner(entityContext);
            var entityScanTargets = new ChunkedCollection<EntityScanTarget>();
            foreach (var key in keys)
            {
                if (key != null)
                {
                    var entityScanTarget =
                        new EntityScanTarget(entityReference, entityReference.GetKeyFromObject(key, false));
                    entityScanTargets.Add(entityScanTarget);
                    entityScanner.Add(entityScanTarget);
                }
            }

            new EntityReader(entityScanner, behaviors, ExpectedFor(keys)).Read();
            return entityScanTargets.Select(entityScanTarget => entityScanTarget.Value);
        }

        /// <summary>
        ///     Reads all entities which belongs to the given database keys from the database.
        /// </summary>
        /// <param name="keys">
        ///     The entity keys.
        /// </param>
        /// <param name="entitySink">
        ///     The entity sink, receives the entities fetched.
        /// </param>
        internal static void Read(EntityContext entityContext, EntityReference entityReference, IEnumerable keys,
            Action<object> entitySink, Behaviors behaviors)
        {
            var entityScanner = new EntityScanner(entityContext);
            var entityScanTargets = new ChunkedCollection<EntityScanTarget>();
            foreach (var key in keys)
            {
                if (key != null)
                {
                    var entityScanTarget = new EntityScanTarget(entityReference,
                        entityReference.GetKeyFromObject(key, false), entitySink);
                    entityScanTargets.Add(entityScanTarget);
                    entityScanner.Add(entityScanTarget);
                }
            }

            new EntityReader(entityScanner, behaviors, ExpectedFor(keys)).Read();
        }

        /// <summary>
        ///     Reads all entities which belongs to the given scan specification from the database.
        /// </summary>
        /// <returns>
        ///     The entities.
        /// </returns>
        internal static IEnumerable Read(EntityContext entityContext, EntityReference entityReference,
            ScanSpec scanSpec, Behaviors behaviors)
        {
            var entityScanResult = new EntityScanResult(entityReference);
            var entityScanner = new EntityScanner(entityContext);
            entityScanner.Add(entityScanResult, scanSpec);

            new EntityReader(entityScanner, behaviors, ExpectedFor(scanSpec)).Read();
            return entityScanResult.Values;
        }

        /// <summary>
        ///     Reads all entities which belongs to the given scan specification from the database.
        /// </summary>
        /// <param name="entitySink">
        ///     The entity sink, receives the entities fetched.
        /// </param>
        internal static void Read(EntityContext entityContext, EntityReference entityReference, ScanSpec scanSpec,
            Action<object> entitySink, Behaviors behaviors)
        {
            var entityScanResult = new EntityScanResult(entityReference, entitySink);
            var entityScanner = new EntityScanner(entityContext);
            entityScanner.Add(entityScanResult, scanSpec);

            new EntityReader(entityScanner, behaviors, ExpectedFor(scanSpec)).Read();
        }

        /// <summary>
        ///     Attempts to get an already fetched entity from the cache.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the cache contains an element with the specified entity spec, otherwise <c>false</c>.
        /// </returns>
        internal bool TryGetFetchedEntity(EntitySpec entitySpec, out object entity)
        {
            return this.entitiesFetched.TryGetValue(entitySpec, out entity);
        }

        /// <param name="cell">
        ///     The fetched cell, if null then key, buffer, length must be provided.
        /// </param>
        /// <param name="key">
        ///     The cell key.
        /// </param>
        /// <param name="buffer">
        ///     The cell buffer.
        /// </param>
        /// <param name="length">
        ///     The cell buffer length.
        /// </param>
        private void EntityFetched(ICell cell, Key key, IntPtr buffer, int length, EntityScanTarget entityScanTarget)
        {
            object entity;

            if (cell != null)
            {
                entity = EntityDeserializer.Deserialize(
                    this,
                    typeof(object),
                    cell.Value,
                    cell.ValueLength,
                    (reference, type, value) =>
                    {
                        this.entitiesFetched.TryAdd(entityScanTarget, value);
                        reference.SetKey(value, cell.Key);
                    });
            }
            else
            {
                entity = EntityDeserializer.Deserialize(
                    this,
                    typeof(object),
                    buffer,
                    length,
                    (reference, type, value) =>
                    {
                        this.entitiesFetched.TryAdd(entityScanTarget, value);
                        reference.SetKey(value, key);
                    });
            }

            // Both of these are reached by several deserializing tasks at once, and both
            // are done under deliveryGate: SetValue because it invokes the caller's setter.
            // The spec set locks on its own, since the manager is shared between threads,
            // and adding to it here costs nothing extra. See deliveryGate
            lock (this.deliveryGate)
            {
                if (!this.behaviors.DoNotCache())
                {
                    this.entitySpecsFetched.Add(new EntitySpec(entityScanTarget));
                }

                if (entity == null || entityScanTarget.EntityType.IsAssignableFrom(entity.GetType()))
                {
                    entityScanTarget.SetValue(entity);
                }
            }
        }

        /// <summary>
        ///     Reads all entities from the entity scanner.
        /// </summary>
        private void Read()
        {
            while (!this.entityScanner.IsEmpty)
            {
                this.entityScanner.Fetch(this.TryGetFetchedEntity, this.EntityFetched);
            }
        }
    }
}
