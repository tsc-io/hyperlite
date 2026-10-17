namespace Hyperlite.Test
{
    using System;
    using System.Text;

    using Hyperlite;

    /// <summary>
    ///     What the table mutator fixtures share.
    /// </summary>
    /// <remarks>
    ///     One table per fixture, named after the fixture, because MSTest runs
    ///     ClassInitialize once per class and no fixture may depend on another having run
    ///     first. The two specs are here because every fixture runs its scenarios through
    ///     them; the unbuffered and queued cases pass null and a spec built inline.
    /// </remarks>
    public abstract class TableMutatorFixture : TestBase
    {
        protected const int Count = 1000;

        protected static readonly MutatorSpec ChunkedMutatorSpec = MutatorSpec.CreateChunked();

        protected static readonly MutatorSpec ChunkedQueuedMutatorSpec =
            new MutatorSpec(MutatorKind.Chunked) { Queued = true, Capacity = 200 };

        protected static readonly UTF8Encoding Encoding = new UTF8Encoding();

        private const string Schema =
            "<Schema><AccessGroup name=\"default\" blksz=\"1024\">" +
            "<ColumnFamily><Name>a</Name></ColumnFamily>" +
            "<ColumnFamily><Name>b</Name></ColumnFamily>" +
            "<ColumnFamily><Name>c</Name></ColumnFamily>" +
            "</AccessGroup></Schema>";

        protected static ITable table;

        /// <summary>
        ///     Creates the fixture's own table. Called from its ClassInitialize.
        /// </summary>
        protected static void CreateTable(Type fixture)
        {
            table = EnsureTable(fixture, Schema);
        }

        /// <summary>
        ///     Drops everything the fixture made. Called from its ClassCleanup.
        /// </summary>
        protected static void DropTable()
        {
            table.Dispose();
            Ns.DropNamespaces(DropDispositions.Complete);
            Ns.DropTables();
        }

        protected static int GetCellCount(ITable target)
        {
            var count = 0;
            using (var scanner = target.CreateScanner())
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    ++count;
                }
            }

            return count;
        }

        protected int GetCellCount()
        {
            return GetCellCount(table);
        }
    }
}
