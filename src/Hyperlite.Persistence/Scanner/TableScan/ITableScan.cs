namespace Hyperlite.Persistence.Scanner.TableScan
{
    using System.Collections.Generic;

    internal interface ITableScan
    {
        IEnumerable<EntityScanTarget> EntityScanTargets { get; }

        /// <summary>
        ///     Gets a value indicating whether there is something to scan or not.
        /// </summary>
        /// <value>
        ///     <c>true</c> if there is something to scan, otherwise <c>false</c>.
        /// </value>
        bool? IsEmpty { get; }

        /// <summary>
        ///     Adds an entity specification to the scan.
        /// </summary>
        /// <param name="entitySpec">
        ///     The entity specification to add.
        /// </param>
        void Add(EntitySpec entitySpec);

        ScanSpec CreateScanSpec();

        /// <summary>
        ///     Try remove a scan target for the entity key specified.
        /// </summary>
        /// <param name="key">
        ///     The entity key.
        /// </param>
        /// <returns>
        ///     <c>true</c> if a scan target as been found for the entity key specified, otherwise <c>false</c>.
        /// </returns>
        bool TryRemoveScanTarget(Key key, out EntityScanTarget entityScanTarget);
    }
}
