namespace Hyperlite.Persistence.Scanner
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.ExceptionServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Hyperlite.Persistence.Collections;
    using Hyperlite.Persistence.Scanner.TableScan;

    internal sealed class EntityScanner
    {
        /// <summary>
        ///     The number of tasks deserializing the fetched cells.
        /// </summary>
        /// <remarks>
        ///     One less than the processors, the scanning thread keeps one busy. The lower bound used
        ///     to be seven, which oversubscribed every machine with fewer than eight cores.
        /// </remarks>
        private static readonly int FetchTaskCount = Math.Max(1, Environment.ProcessorCount - 1);

        /// <summary>
        ///     Number of cells handed to a deserializing task at once.
        /// </summary>
        /// <remarks>
        ///     The cells used to be queued one by one, which took the queue lock and signalled the
        ///     waiting tasks per cell.
        /// </remarks>
        private const int FetchBatchSize = 64;

        private readonly EntityContext entityContext;

        private readonly object syncRoot = new object();

        /// <summary>
        ///     Indicating whether to use parallel deserialization or not.
        /// </summary>
        private readonly bool useParallelDeserialization;

        private Map<Pair<string>, ITableScan> tables;

        internal EntityScanner(EntityContext entityContext)
        {
            this.entityContext = entityContext;
            this.useParallelDeserialization = entityContext.Configuration.UseParallelDeserialization;
        }

        internal EntityContext EntityContext => this.entityContext;

        /// <summary>
        ///     Gets a value indicating whether there is something to scan or not.
        /// </summary>
        /// <value>
        ///     <c>true</c> if there is something to scan, otherwise <c>false</c>.
        /// </value>
        internal bool IsEmpty
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.tables == null;
                }
            }
        }

        /// <summary>
        ///     Adds an entity scan target to the entity scanner.
        /// </summary>
        /// <param name="entityScanTarget">
        ///     The entity scan target to add.
        /// </param>
        internal void Add(EntityScanTarget entityScanTarget)
        {
            lock (this.syncRoot)
            {
                if (this.tables == null)
                {
                    this.tables = new Map<Pair<string>, ITableScan>();
                }

                var tableScanSpec =
                    this.tables.GetOrAdd(
                        new Pair<string>(entityScanTarget.Namespace, entityScanTarget.TableName),
                        kvp => this.CreateTableScanAndFilter());
                tableScanSpec.Add(entityScanTarget);
            }
        }

        /// <summary>
        ///     Adds an entity specification to the scan.
        /// </summary>
        /// <param name="entitySpec">
        ///     The entity specification to add.
        /// </param>
        /// <param name="scanSpec">
        ///     The scan specification.
        /// </param>
        internal void Add(EntitySpec entitySpec, ScanSpec scanSpec)
        {
            lock (this.syncRoot)
            {
                // a scan spec replaces whatever was queued before, the map used to be allocated
                // twice here
                this.tables = new Map<Pair<string>, ITableScan>();

                var tableScanSpec = this.tables.GetOrAdd(
                    new Pair<string>(entitySpec.Namespace, entitySpec.TableName),
                    kvp => this.CreateTableScan(scanSpec));
                tableScanSpec.Add(entitySpec);
            }
        }

        /// <summary>
        ///     Fetches all the scan targets.
        /// </summary>
        /// <param name="entityFetched">
        ///     The entity fetched delegate.
        /// </param>
        internal void Fetch(TryGetFetchedEntity tryGetFetchedEntity, EntityFetched entityFetched)
        {
            IEnumerable<KeyValuePair<Pair<string>, ITableScan>> tablesToFetch;
            lock (this.syncRoot)
            {
                tablesToFetch = this.tables;
                this.tables = null;
            }

            foreach (var tableItem in tablesToFetch)
            {
                var tableScan = tableItem.Value;
                foreach (var entityScanTarget in tableScan.EntityScanTargets.ToList())
                {
                    object entity;
                    if (tryGetFetchedEntity(entityScanTarget, out entity))
                    {
                        if (entity == null || entityScanTarget.EntityType.IsAssignableFrom(entity.GetType()))
                        {
                            entityScanTarget.SetValue(entity);
                        }

                        EntityScanTarget removedEntityScanTarget;
                        tableScan.TryRemoveScanTarget(entityScanTarget.Key, out removedEntityScanTarget);
                    }
                }
            }

            tablesToFetch = tablesToFetch.Where(kv => kv.Value.IsEmpty.IsNullOrFalse()).ToList();

            var reviewScanSpec = this.entityContext.Configuration.ReviewScanSpec;

            ////TODO compare async vs sync on multi-core
            if (this.useParallelDeserialization)
            {
                foreach (var tableItem in tablesToFetch)
                {
                    var table = this.entityContext.GetTable(tableItem.Key.First, tableItem.Key.Second);
                    if (table == null)
                    {
                        throw new PersistenceException(
                            string.Format(CultureInfo.InvariantCulture, @"Table {0}/{1} does not exist",
                                tableItem.Key.First.TrimEnd('/'), tableItem.Key.Second));
                    }

                    var scanSpec = tableItem.Value.CreateScanSpec();

                    if (reviewScanSpec != null)
                    {
                        reviewScanSpec(table, scanSpec);
                    }

                    //// TODO add/remove more infos + time, other places???
                    if (Logging.IsEnabled(TraceEventType.Verbose))
                    {
                        Logging.TraceEvent(
                            TraceEventType.Verbose,
                            () => string.Format(CultureInfo.InvariantCulture, @"Scan {0} on table {1}", scanSpec,
                            table.Name));
                    }

                    if (scanSpec.RowCount == 0 || scanSpec.RowCount > 32)
                    {
                        var fetchedCells = new BlockingQueue<FetchedCell[]>();

                        // set by a consumer that failed: the fetch fails with it, so the scan
                        // stops rather than queueing the rest of the table to nobody
                        var consumerFailed = 0;

                        Action ProcessFetchedCells = () =>
                        {
                            try
                            {
                                Consume();
                            }
                            catch (Exception)
                            {
                                Volatile.Write(ref consumerFailed, 1);
                                throw;
                            }
                        };

                        void Consume()
                        {
                            while (true)
                            {
                                var batch = fetchedCells.Dequeue();
                                if (batch == null)
                                {
                                    break;
                                }

                                foreach (var fetchedCell in batch)
                                {
                                    if (fetchedCell == null)
                                    {
                                        break; // a partial batch, the rest of the slots are empty
                                    }

                                    try
                                    {
                                        // once one consumer has failed the fetch fails with it,
                                        // so what is still queued is only returned to the pool
                                        if (Volatile.Read(ref consumerFailed) == 0)
                                        {
                                            entityFetched(fetchedCell, null, IntPtr.Zero, 0, fetchedCell.EntityScanTarget);
                                        }
                                    }
                                    finally
                                    {
                                        PooledCell.Return(fetchedCell.Value);
                                    }
                                }
                            }
                        }

                        var tasks = new Task[FetchTaskCount];
                        for (var i = 0; i < tasks.Length; ++i)
                        {
                            tasks[i] = Task.Run(ProcessFetchedCells);
                        }

                        var scanCompleted = false;
                        try
                        {
                            using (var scanner = table.CreateScanner(scanSpec))
                            {
                                var fetchedCell = new FetchedCell();
                                var batch = new FetchedCell[FetchBatchSize];
                                var batched = 0;
                                while (Volatile.Read(ref consumerFailed) == 0 && scanner.Move(fetchedCell))
                                {
                                    if (tableItem.Value.TryRemoveScanTarget(fetchedCell.Key, out fetchedCell.EntityScanTarget))
                                    {
                                        batch[batched++] = fetchedCell;
                                        fetchedCell = new FetchedCell();
                                        if (batched == FetchBatchSize)
                                        {
                                            fetchedCells.Enqueue(batch);
                                            batch = new FetchedCell[FetchBatchSize];
                                            batched = 0;
                                        }
                                    }
                                    else
                                    {
                                        PooledCell.Return(fetchedCell.Value);
                                    }
                                }

                                if (batched > 0)
                                {
                                    fetchedCells.Enqueue(batch);
                                }

                                if (tableItem.Value.IsEmpty.IsFalse())
                                {
                                    //// TODO remaining cells should be deleted
                                }
                            }

                            scanCompleted = true;
                        }
                        finally
                        {
                            // Every consumer waits for one sentinel. A scan which throws would leave
                            // them blocked for the lifetime of the process, still holding whatever is
                            // queued, so the sentinels go out either way
                            for (var i = 0; i < tasks.Length; ++i)
                            {
                                fetchedCells.Enqueue(null);
                            }

                            if (scanCompleted)
                            {
                                try
                                {
                                    Task.WaitAll(tasks);
                                }
                                catch (AggregateException aggregateException)
                                {
                                    // what a consumer failed with, as the sequential path throws
                                    // it, rather than a wrapper whose presence depends on the scan
                                    ExceptionDispatchInfo.Capture(aggregateException.Flatten().InnerExceptions[0]).Throw();
                                }
                            }
                            else
                            {
                                // already unwinding, a consumer failure must not mask the scan error
                                try
                                {
                                    Task.WaitAll(tasks);
                                }
                                catch (AggregateException aggregateException)
                                {
                                    Logging.TraceException(aggregateException);
                                }
                            }
                        }
                    }
                    else
                    {
                        Func<Key, IntPtr, int, bool> callback = (key, buffer, length) =>
                        {
                            EntityScanTarget entityScanTarget;
                            if (tableItem.Value.TryRemoveScanTarget(key, out entityScanTarget))
                            {
                                entityFetched(null, key, buffer, length, entityScanTarget);
                            }

                            return true;
                        };

                        using (var scanner = table.CreateScanner(scanSpec))
                        {
                            while (scanner.Next(callback))
                                ;

                            if (tableItem.Value.IsEmpty.IsFalse())
                            {
                                //// TODO remaining cells should be deleted
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (var tableItem in tablesToFetch)
                {
                    var table = this.entityContext.GetTable(tableItem.Key.First, tableItem.Key.Second);
                    if (table == null)
                    {
                        throw new PersistenceException(
                            string.Format(CultureInfo.InvariantCulture, @"Table {0}/{1} does not exist",
                                tableItem.Key.First.TrimEnd('/'), tableItem.Key.Second));
                    }

                    var scanSpec = tableItem.Value.CreateScanSpec();

                    if (reviewScanSpec != null)
                    {
                        reviewScanSpec(table, scanSpec);
                    }

                    //// TODO add/remove more infos + time, other places???
                    if (Logging.IsEnabled(TraceEventType.Verbose))
                    {
                        Logging.TraceEvent(
                            TraceEventType.Verbose,
                            () => string.Format(CultureInfo.InvariantCulture, @"Scan {0} on table {1}", scanSpec,
                            table.Name));
                    }

                    Func<Key, IntPtr, int, bool> callback = (key, buffer, length) =>
                    {
                        EntityScanTarget entityScanTarget;
                        if (tableItem.Value.TryRemoveScanTarget(key, out entityScanTarget))
                        {
                            entityFetched(null, key, buffer, length, entityScanTarget);
                        }

                        return true;
                    };

                    using (var scanner = table.CreateScanner(scanSpec))
                    {
                        while (scanner.Next(callback))
                            ;

                        if (tableItem.Value.IsEmpty.IsFalse())
                        {
                            //// TODO remaining cells should be deleted
                        }
                    }
                }
            }
        }

        /// <returns>
        ///     The newly created table scan instance.
        /// </returns>
        private ITableScan CreateTableScan(ScanSpec scanSpec)
        {
            return new TableScan.TableScan(scanSpec);
        }

        /// <returns>
        ///     The newly created table scan and filter instance.
        /// </returns>
        private ITableScan CreateTableScanAndFilter()
        {
            // Parallel deserialization removes scan targets on the scanning thread
            // alone - its tasks only deserialize - so the concurrent filter is not
            // needed. It existed for the asynchronous scanner, which is gone.
            return new TableScanAndFilter();
        }

        private sealed class BlockingQueue<T>
        {
            private int count;

            private readonly Queue<T> queue = new Queue<T>();

            public T Dequeue()
            {
                lock (this.queue)
                {
                    while (this.count <= 0)
                    {
                        Monitor.Wait(this.queue);
                    }

                    this.count--;

                    return this.queue.Dequeue();
                }
            }

            public void Enqueue(T data)
            {
                lock (this.queue)
                {
                    this.queue.Enqueue(data);

                    this.count++;

                    Monitor.Pulse(this.queue);
                }
            }
        }
    }
}
