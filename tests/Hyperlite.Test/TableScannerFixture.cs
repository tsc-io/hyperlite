namespace Hyperlite.Test
{
    using System;
    using System.Text;

    using Hyperlite;

    /// <summary>
    ///     The table the scanner fixtures read, and the schema behind it.
    /// </summary>
    /// <remarks>
    ///     Every fixture seeds a table of its own rather than sharing one. That is not
    ///     caution: fixtures elsewhere in this assembly drop every table in the namespace in
    ///     their ClassCleanup, so a table shared between classes would be emptied by
    ///     whichever class ran in between. Seeding costs about 0.6s per fixture, measured.
    ///     <para>
    ///     Families a, b and c hold the seed and no case writes to them. Families d to g are
    ///     scratch, emptied before each case by
    ///     <see cref="ResetScratchColumnFamilies" />.
    ///     </para>
    /// </remarks>
    public abstract class TableScannerFixture : TestBase
    {
        /// <summary>
        ///     Rows in family a, and so the number of rows in the table.
        /// </summary>
        protected const int CountA = 100000;

        /// <summary>
        ///     How many of those rows also carry a cell in family b.
        /// </summary>
        protected const int CountB = 10000;

        /// <summary>
        ///     How many of those rows also carry a cell in family c.
        /// </summary>
        protected const int CountC = 1000;

        /// <summary>
        ///     Rows a case builds for itself when it needs a table it can predict.
        /// </summary>
        protected const int R = 100;

        protected const string Schema =
            "<Schema><AccessGroup name=\"default\" blksz=\"1024\">" +
            "<ColumnFamily><Name>a</Name></ColumnFamily>" +
            "<ColumnFamily><Name>b</Name></ColumnFamily>" +
            "<ColumnFamily><Name>c</Name></ColumnFamily>" +
            "<ColumnFamily><Name>d</Name></ColumnFamily>" +
            "<ColumnFamily><Name>e</Name></ColumnFamily>" +
            "<ColumnFamily><Name>f</Name></ColumnFamily>" +
            "<ColumnFamily><Name>g</Name></ColumnFamily>" +
            "</AccessGroup></Schema>";

        protected static readonly UTF8Encoding Encoding = new UTF8Encoding();

        protected static ITable table;

        /// <summary>
        ///     Creates the fixture's table and fills it. Called from its ClassInitialize.
        /// </summary>
        protected static void CreateAndSeedTable(Type fixture)
        {
            table = EnsureTable(fixture, Schema);
            Seed(table);
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

        /// <summary>
        ///     Empties the scratch families, so each case starts from the seed alone.
        /// </summary>
        protected static void ResetScratchColumnFamilies()
        {
            DeleteColumnFamily(table, "d");
            DeleteColumnFamily(table, "e");
            DeleteColumnFamily(table, "f");
            DeleteColumnFamily(table, "g");
        }

        private static void Seed(ITable target)
        {
            var key = new Key();
            using (var mutator = target.CreateMutator(MutatorSpec.CreateChunked()))
            {
                for (var n = 0; n < CountA; ++n)
                {
                    key.ColumnFamily = "a";
                    key.Row = Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));

                    if (n < CountB)
                    {
                        key.ColumnFamily = "b";
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                    }

                    if (n < CountC)
                    {
                        key.ColumnFamily = "c";
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                    }
                }
            }
        }
    }
}
