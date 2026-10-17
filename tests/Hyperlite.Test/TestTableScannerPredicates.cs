namespace Hyperlite.Test
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Selecting on the value rather than on the key.
    /// </summary>
    /// <remarks>
    ///     The predicate cases are the largest in the suite and the ones most likely to differ
    ///     between providers, since a predicate is either pushed into the query or applied after
    ///     it. Both paths have to return the same cells.
    ///     <para>
    ///     The table is this fixture's own, seeded in ClassInitialize: CountA rows in family a,
    ///     the first CountB of them also in b and the first CountC also in c. Families d to g are
    ///     scratch - a case may write what it needs into them, and TestInitialize empties them
    ///     again, which is why a case may assume they start empty.
    ///     </para>
    /// </remarks>
    [TestClass]
    public sealed class TestTableScannerPredicates : TableScannerFixture
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            CreateAndSeedTable(typeof(TestTableScannerPredicates));
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            DropTable();
        }

        [TestInitialize]
        public void TestInitialize()
        {
            ContinueExecution();
            ResetScratchColumnFamilies();
        }

        /// <summary>
        ///     An exact value predicate over one column family.
        ///     Two of the rows hold an empty value and a null one, which the provider cannot
        ///     tell apart - hence two hits where one might be expected.
        /// </summary>
        [TestMethod]
        public void ValueExactPredicate()
        {
            WriteValueRows("d", "What a wonderful world");

            var scanSpec = new ScanSpec()
                .AddColumn("d")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValueExact, Encoding.GetBytes("XYY")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(1, c);
            }

            // Cannot distinguish between 'null' and 'string.Empty'
            scanSpec = new ScanSpec()
                .AddColumn("d")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValueExact, Encoding.GetBytes(string.Empty)));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.IsNull(cell.Value);
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            // Cannot distinguish between 'null' and 'string.Empty'
            scanSpec = new ScanSpec()
                .AddColumn("d")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValueExact, null));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.IsNull(cell.Value);
                    ++c;
                }

                Assert.AreEqual(2, c);
            }
        }

        /// <summary>
        ///     A value prefix predicate over one column family, from the empty prefix, which
        ///     matches everything, to one that matches nothing.
        /// </summary>
        [TestMethod]
        public void ValuePrefixPredicate()
        {
            WriteValueRows("d", "What a wonderful world");

            var scanSpec = new ScanSpec()
                .AddColumn("d")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValuePrefix, Encoding.GetBytes(string.Empty)));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(7, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValuePrefix, null));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(7, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValuePrefix, Encoding.GetBytes("X")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(3, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValuePrefix, Encoding.GetBytes("XX")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValuePrefix, Encoding.GetBytes("Y")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(1, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValuePrefix, Encoding.GetBytes("ZYX")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(0, c);
            }
        }

        /// <summary>
        ///     A value regular expression over one column family.
        /// </summary>
        [TestMethod]
        public void ValueRegexPredicate()
        {
            WriteValueRows("d", "What a wonderful world");

            var scanSpec = new ScanSpec()
                .AddColumn("d")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValueRegex, Encoding.GetBytes("wonder")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual("ZZZ", cell.Key.Row);
                    ++c;
                }

                Assert.AreEqual(1, c);
            }
        }

        /// <summary>
        ///     The same predicates applied to two column families at once, each family holding
        ///     its own seven rows. A predicate names the family it applies to, so a scan over
        ///     both carries one for each.
        /// </summary>
        [TestMethod]
        public void ValuePredicatesOnTwoFamilies()
        {
            WriteValueRows("d", "What a wonderful world");
            WriteValueRows("e", "What's going on?");

            var scanSpec = new ScanSpec()
                .AddColumn("d", "e")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValueExact, Encoding.GetBytes("XYY")))
                .AddColumnPredicate(new ColumnPredicate("e", MatchKind.ValueExact, Encoding.GetBytes("XYY")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d", "e")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValuePrefix, Encoding.GetBytes("X")))
                .AddColumnPredicate(new ColumnPredicate("e", MatchKind.ValuePrefix, Encoding.GetBytes("X")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(6, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d", "e")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValuePrefix, Encoding.GetBytes("XX")))
                .AddColumnPredicate(new ColumnPredicate("e", MatchKind.ValuePrefix, Encoding.GetBytes("XX")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(4, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d", "e")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValuePrefix, Encoding.GetBytes("Y")))
                .AddColumnPredicate(new ColumnPredicate("e", MatchKind.ValuePrefix, Encoding.GetBytes("Y")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d", "e")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValueExact, Encoding.GetBytes("ZYX")))
                .AddColumnPredicate(new ColumnPredicate("e", MatchKind.ValueExact, Encoding.GetBytes("ZYX")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(0, c);
            }

            // Cannot distinguish between 'null' and 'string.Empty'
            scanSpec = new ScanSpec()
                .AddColumn("d", "e")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValueExact, Encoding.GetBytes(string.Empty)))
                .AddColumnPredicate(new ColumnPredicate("e", MatchKind.ValueExact, Encoding.GetBytes(string.Empty)));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("ZZ"));
                    Assert.IsNull(cell.Value);
                    ++c;
                }

                Assert.AreEqual(4, c);
            }

            // Cannot distinguish between 'null' and 'string.Empty'
            scanSpec = new ScanSpec()
                .AddColumn("d", "e")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValueExact, null))
                .AddColumnPredicate(new ColumnPredicate("e", MatchKind.ValueExact, null));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("ZZ"));
                    Assert.IsNull(cell.Value);
                    ++c;
                }

                Assert.AreEqual(4, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d", "e")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValuePrefix, Encoding.GetBytes(string.Empty)))
                .AddColumnPredicate(new ColumnPredicate("e", MatchKind.ValuePrefix, Encoding.GetBytes(string.Empty)));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(14, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d", "e")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValuePrefix, null))
                .AddColumnPredicate(new ColumnPredicate("e", MatchKind.ValuePrefix, null));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(14, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d", "e")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValueRegex, Encoding.GetBytes("wonder")))
                .AddColumnPredicate(new ColumnPredicate("e", MatchKind.ValueRegex, Encoding.GetBytes("What's")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual("ZZZ", cell.Key.Row);
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("d", "e")
                .AddColumnPredicate(new ColumnPredicate("d", MatchKind.ValueRegex, Encoding.GetBytes("on")))
                .AddColumnPredicate(new ColumnPredicate("e", MatchKind.ValueRegex, Encoding.GetBytes("on")));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual("ZZZ", cell.Key.Row);
                    ++c;
                }

                Assert.AreEqual(2, c);
            }
        }

        /// <summary>
        ///     Predicates on the column qualifier rather than on the value: exact, prefix and
        ///     regular expression. The empty qualifier and the null one are again
        ///     indistinguishable to the provider.
        /// </summary>
        [TestMethod]
        public void QualifierPredicates()
        {
            var key = new Key { Row = "XXX", ColumnFamily = "f", ColumnQualifier = "AAA" };
            using (var mutator = table.CreateMutator())
            {
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "XXY";
                key.ColumnQualifier = "AAB";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "XYY";
                key.ColumnQualifier = "ABB";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "YYY";
                key.ColumnQualifier = "BBB";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "ZZX";
                key.ColumnQualifier = string.Empty;
                mutator.Set(key, Encoding.GetBytes(string.Empty));
                key.Row = "ZZY";
                key.ColumnQualifier = null;
                mutator.Set(key, null);
                key.Row = "ZZZ";
                key.ColumnQualifier = "BBC";
                mutator.Set(key, Encoding.GetBytes("What a wonderful world"));
            }

            var scanSpec = new ScanSpec()
                .AddColumn("f")
                .AddColumnPredicate(new ColumnPredicate("f", "ABB", MatchKind.QualifierExact));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(1, c);
            }

            // Cannot distinguish between 'null' and 'string.Empty'
            scanSpec = new ScanSpec()
                .AddColumn("f")
                .AddColumnPredicate(new ColumnPredicate("f", string.Empty, MatchKind.QualifierExact));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.IsNull(cell.Value);
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            // Cannot distinguish between 'null' and 'string.Empty'
            scanSpec = new ScanSpec()
                .AddColumn("f")
                .AddColumnPredicate(new ColumnPredicate("f", null, MatchKind.QualifierExact));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.IsNull(cell.Value);
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("f")
                .AddColumnPredicate(new ColumnPredicate("f", "A", MatchKind.QualifierPrefix));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(3, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("f")
                .AddColumnPredicate(new ColumnPredicate("f", "BB", MatchKind.QualifierPrefix));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("f")
                .AddColumnPredicate(new ColumnPredicate("f", string.Empty, MatchKind.QualifierPrefix));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(7, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("f")
                .AddColumnPredicate(new ColumnPredicate("f", null, MatchKind.QualifierPrefix));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(7, c);
            }

            scanSpec = new ScanSpec()
                .AddColumn("f")
                .AddColumnPredicate(new ColumnPredicate("f", "A[A|B]B", MatchKind.QualifierRegex));

            using (var scanner = table.CreateScanner(scanSpec))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(2, c);
            }
        }

        /// <summary>
        ///     Writes the seven rows the value predicate cases scan.
        /// </summary>
        /// <remarks>
        ///     Two of them are the interesting ones: ZZX holds an empty value and ZZY holds
        ///     null, and the provider cannot tell those apart - which is what the cases
        ///     asserting two hits for an exact match on either are recording.
        /// </remarks>
        /// <param name="columnFamily">
        ///     The family to write into.
        /// </param>
        /// <param name="sentence">
        ///     The value of row ZZZ, which the regex cases match on.
        /// </param>
        private static void WriteValueRows(string columnFamily, string sentence)
        {
            var key = new Key { Row = "XXX", ColumnFamily = columnFamily };
            using (var mutator = table.CreateMutator())
            {
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "XXY";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "XYY";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "YYY";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "ZZX";
                mutator.Set(key, Encoding.GetBytes(string.Empty));
                key.Row = "ZZY";
                mutator.Set(key, null);
                key.Row = "ZZZ";
                mutator.Set(key, Encoding.GetBytes(sentence));
            }
        }

        [TestMethod]
        public void ValueIndex()
        {
            const string ScanTableValueIndexSchema =
                "<Schema><AccessGroup name=\"default\" blksz=\"1024\">" +
                "<ColumnFamily><Name>a</Name><Index>true</Index></ColumnFamily>" +
                "<ColumnFamily><Name>b</Name><Index>true</Index></ColumnFamily>" +
                "<ColumnFamily><Name>c</Name><Index>true</Index></ColumnFamily>" +
                "</AccessGroup></Schema>";

            const int Count = 10000;
            using (var _table = EnsureTable("ValueIndex", ScanTableValueIndexSchema))
            {
                var key = new Key();
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < Count; ++i)
                    {
                        var value = Encoding.GetBytes(i.ToString());

                        key.ColumnFamily = "a";
                        key.Row = null;
                        mutator.Set(key, value);

                        key.ColumnFamily = "b";
                        key.Row = null;
                        mutator.Set(key, value);

                        key.ColumnFamily = "c";
                        key.Row = null;
                        mutator.Set(key, value);
                    }
                }

                string[] columnFamilies = { "a", "b", "c" };
                var rng = new Random();
                for (var i = 0; i < Count / 100; ++i)
                {
                    var search = rng.Next(Count).ToString();

                    var column = columnFamilies[rng.Next(columnFamilies.Length)];
                    var scanSpec = new ScanSpec()
                        .AddColumn(column)
                        .AddColumnPredicate(new ColumnPredicate(column, MatchKind.ValueExact, Encoding.GetBytes(search)));

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        Cell cell;
                        Assert.IsTrue(scanner.Next(out cell));
                        Assert.AreEqual(search, Encoding.GetString(cell.Value));
                        Assert.IsFalse(scanner.Next(out cell));
                    }
                }

                int[] ranges = { 1000, 300, 56, 9999, 789, 4 };
                int[] occurrence = { 1, 11, 111, 1, 11, 1111 };
                Assert.AreEqual(ranges.Length, occurrence.Length);
                for (var i = 0; i < ranges.Length; ++i)
                {
                    var search = ranges[i].ToString();

                    var column = columnFamilies[rng.Next(columnFamilies.Length)];
                    var scanSpec = new ScanSpec()
                        .AddColumn(column)
                        .AddColumnPredicate(new ColumnPredicate(column, MatchKind.ValuePrefix, Encoding.GetBytes(search)));

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        var c = 0;
                        var cell = new Cell();
                        while (scanner.Move(cell))
                        {
                            Assert.IsTrue(Encoding.GetString(cell.Value).StartsWith(search));
                            ++c;
                        }

                        Assert.AreEqual(occurrence[i], c, $"range = {ranges[i]}");
                    }
                }
            }
        }

        [TestMethod]
        public void ValueRegex()
        {
            var key = new Key { Row = "XXX", ColumnFamily = "d" };
            using (var mutator = table.CreateMutator())
            {
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "XXY";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "XYY";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "YYY";
                mutator.Set(key, Encoding.GetBytes(key.Row));
            }

            using (var scanner = table.CreateScanner(new ScanSpec { ValueRegex = "X.*" }.AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(3, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { ValueRegex = "XX." }.AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { ValueRegex = "XY.*" }.AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { ValueRegex = "^Y.*" }.AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(1, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { ValueRegex = "^YY.*" }.AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(1, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { ValueRegex = "^Y.." }.AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(1, c);
            }
        }
    }
}
