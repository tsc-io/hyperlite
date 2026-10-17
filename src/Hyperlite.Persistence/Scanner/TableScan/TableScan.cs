namespace Hyperlite.Persistence.Scanner.TableScan
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    ///     The simple table scan.
    /// </summary>
    internal sealed class TableScan : ITableScan
    {
        private readonly ScanSpec scanSpec;

        private readonly object syncRoot = new object();

        private EntityScanResult entityScanResult;

        /// <exception cref="ArgumentNullException">
        ///     Id the <paramref name="scanSpec" /> is null.
        /// </exception>
        internal TableScan(ScanSpec scanSpec)
        {
            if (scanSpec == null)
            {
                throw new ArgumentNullException(nameof(scanSpec));
            }

            this.scanSpec = scanSpec;
        }

        public IEnumerable<EntityScanTarget> EntityScanTargets => Enumerable.Empty<EntityScanTarget>();

        /// <inheritdoc />
        public bool? IsEmpty => null;

        /// <inheritdoc />
        public void Add(EntitySpec entitySpec)
        {
            var esr = entitySpec as EntityScanResult;
            if (esr == null)
            {
                throw new ArgumentException(@"EntitySpec of type EntityScanResult expected", nameof(entitySpec));
            }

            lock (this.syncRoot)
            {
                this.entityScanResult = esr;
            }
        }

        public ScanSpec CreateScanSpec()
        {
            return this.scanSpec;
        }

        /// <summary>
        ///     Try get a scan target for the entity key specified.
        /// </summary>
        /// <param name="key">
        ///     The entity key.
        /// </param>
        /// <returns>
        ///     <c>true</c> if a scan target as been found for the entity key specified, otherwise <c>false</c>.
        /// </returns>
        public bool TryRemoveScanTarget(Key key, out EntityScanTarget entityScanTarget)
        {
            lock (this.syncRoot)
            {
                entityScanTarget = new EntityQueryScanTarget(this.entityScanResult.EntityType, this.entityScanResult,
                    key, this.entityScanResult.ValueSink);
            }

            return true;
        }
    }
}
