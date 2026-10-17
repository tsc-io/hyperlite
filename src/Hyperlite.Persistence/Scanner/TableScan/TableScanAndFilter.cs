namespace Hyperlite.Persistence.Scanner.TableScan
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Hyperlite.Persistence.Extensions;

    //// TODO scan&filter depends on table row count vs query row count
    //// scan hints, track recent row count? Check if switch ScanAndFilter off....

    internal abstract class TableScanAndFilterBase : ITableScan
    {
        /// <summary>
        ///     Initial capacity of the scan target dictionaries.
        /// </summary>
        /// <remarks>
        ///     One of these is built per fetch, and a fetch is often a single Find. Starting at 256
        ///     allocated and cleared roughly 6kB per dictionary to look up one row; they grow when a
        ///     FindMany actually needs the room.
        /// </remarks>
        protected const int ScanTargetCapacity = 16;

        public abstract IEnumerable<EntityScanTarget> EntityScanTargets { get; }

        /// <summary>
        ///     Gets a value indicating whether is empty.
        /// </summary>
        public abstract bool? IsEmpty { get; }

        /// <inheritdoc />
        public void Add(EntitySpec entitySpec)
        {
            var entityScanTarget = entitySpec as EntityScanTarget;
            if (entityScanTarget == null)
            {
                throw new ArgumentException(@"EntitySpec of type EntityScanTarget expected", nameof(entitySpec));
            }

            EntityScanTarget entityScanTargetExisting;
            if (!this.GetOrAdd(entityScanTarget, out entityScanTargetExisting))
            {
                entityScanTargetExisting.AddScanTargetRef(entityScanTarget);
            }
        }

        public ScanSpec CreateScanSpec()
        {
            var scanSpec = new ScanSpec { MaxVersions = 1, ScanAndFilter = true };
            foreach (var entityScanTarget in this.EntityScanTargets)
            {
                var key = entityScanTarget.Key;
                if (!string.IsNullOrEmpty(key.ColumnFamily))
                {
                    if (key.ColumnQualifier == null)
                    {
                        scanSpec.AddColumn(key.ColumnFamily);
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        sb.Append(key.ColumnFamily);
                        sb.Append(":");
                        sb.Append(key.ColumnQualifier);
                        scanSpec.AddColumn(sb.ToString());
                    }
                }

                scanSpec.AddRow(key.Row);
            }

            return scanSpec;
        }

        /// <inheritdoc />
        public abstract bool TryRemoveScanTarget(Key key, out EntityScanTarget entityScanTarget);

        /// <summary>
        ///     Gets or adds an entity scan target.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if a scan target has been added, otherwise <c>false</c>.
        /// </returns>
        protected abstract bool GetOrAdd(EntityScanTarget entityScanTarget,
            out EntityScanTarget entityScanTargetExisting);
    }

    internal sealed class TableScanAndFilter : TableScanAndFilterBase
    {
        /// <summary>
        ///     The entity keys.
        /// </summary>
        private readonly Dictionary<Key, EntityScanTarget> keys =
            new Dictionary<Key, EntityScanTarget>(ScanTargetCapacity, new KeyComparer());

        /// <summary>
        ///     The unqualified entity keys.
        /// </summary>
        private readonly Dictionary<Key, EntityScanTarget> unqualifiedKeys =
            new Dictionary<Key, EntityScanTarget>(ScanTargetCapacity, new RowComparer());

        public override IEnumerable<EntityScanTarget> EntityScanTargets => this.keys.Values.Concat(this.unqualifiedKeys.Values);

        /// <summary>
        ///     Gets a value indicating whether there is something to scan or not.
        /// </summary>
        /// <value>
        ///     <c>true</c> if there is something to scan, otherwise <c>false</c>.
        /// </value>
        public override bool? IsEmpty => this.unqualifiedKeys.Count == 0 && this.keys.Count == 0;

        /// <summary>
        ///     Try get a scan target for the entity key specified.
        /// </summary>
        /// <param name="key">
        ///     The entity key.
        /// </param>
        /// <returns>
        ///     <c>true</c> if a scan target as been found for the entity key specified, otherwise <c>false</c>.
        /// </returns>
        public override bool TryRemoveScanTarget(Key key, out EntityScanTarget entityScanTarget)
        {
            if (!PersistenceConfiguration.UseUnqualifiedKeys)
            {
                if (this.keys.TryRemove(key, out entityScanTarget))
                {
                    return true;
                }
            }

            return this.unqualifiedKeys.TryRemove(key, out entityScanTarget);
        }

        /// <inheritdoc />
        protected override bool GetOrAdd(EntityScanTarget entityScanTarget,
            out EntityScanTarget entityScanTargetExisting)
        {
            if (entityScanTarget == null)
            {
                throw new ArgumentNullException(nameof(entityScanTarget));
            }

            var added = false;

            if (PersistenceConfiguration.UseUnqualifiedKeys || string.IsNullOrEmpty(entityScanTarget.Key.ColumnFamily))
            {
                entityScanTargetExisting = this.unqualifiedKeys.GetOrAdd(
                    entityScanTarget.Key,
                    key =>
                    {
                        added = true;
                        return entityScanTarget;
                    });
            }
            else
            {
                entityScanTargetExisting = this.keys.GetOrAdd(
                    entityScanTarget.Key,
                    key =>
                    {
                        added = true;
                        return entityScanTarget;
                    });
            }

            return added;
        }
    }
}
