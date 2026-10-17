namespace Hyperlite.Test
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Choosing cells: intervals and offsets over cells, and selection by column family
    ///     and qualifier.
    /// </summary>
    /// <remarks>
    ///     The table is this fixture's own, seeded in ClassInitialize: CountA rows in family a,
    ///     the first CountB of them also in b and the first CountC also in c. Families d to g are
    ///     scratch - a case may write what it needs into them, and TestInitialize empties them
    ///     again, which is why a case may assume they start empty.
    /// </remarks>
    [TestClass]
    public sealed class TestTableScannerCellSelection : TableScannerFixture
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            CreateAndSeedTable(typeof(TestTableScannerCellSelection));
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
        ///     Every way the bounds of a cell interval can be given: an open start, an empty
        ///     start, an open end, an empty end, both closed, inclusive and exclusive, and two
        ///     intervals in one scan spec.
        /// </summary>
        [TestMethod]
        public void CellIntervalBounds()
        {
            using (var _table = EnsureTable("CellInterval", Schema))
            {
                var key = new Key { ColumnFamily = "d" };
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval(null, "d", "50", "d"))))
                {
                    var c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        ++c;
                    }

                    Assert.AreEqual(51, c);
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval(string.Empty, "d", "50", "d"))))
                {
                    var c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        ++c;
                    }

                    Assert.AreEqual(51, c);
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("90", "d", null, "d"))))
                {
                    var c = 90;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        ++c;
                    }

                    Assert.AreEqual(100, c);
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("90", "d", string.Empty, "d"))))
                {
                    var c = 90;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        ++c;
                    }

                    Assert.AreEqual(100, c);
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("10", "d", "50", "d"))))
                {
                    var c = 10;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        ++c;
                    }

                    Assert.AreEqual(51, c);
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("10", "d", null, true, "50", "d", null, false))))
                {
                    var c = 10;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        ++c;
                    }

                    Assert.AreEqual(50, c);
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("10", "d", null, false, "50", "d", null, false))))
                {
                    var c = 11;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        ++c;
                    }

                    Assert.AreEqual(50, c);
                }

                using (var scanner = _table.CreateScanner(new ScanSpec().AddCellInterval(new CellInterval("10", "d", "20", "d"), new CellInterval("40", "d", "50", "d"))))
                {
                    var c = 10;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        if (c != 20)
                        {
                            ++c;
                        }
                        else
                        {
                            c = 40;
                        }
                    }

                    Assert.AreEqual(51, c);
                }
            }
        }

        /// <summary>
        ///     An interval whose start names one column family and whose end names another, so
        ///     each row in range returns a cell from both.
        /// </summary>
        [TestMethod]
        public void CellIntervalSpanningTwoFamilies()
        {
            using (var _table = EnsureTable("CellInterval", Schema))
            {
                var key = new Key { ColumnFamily = "d" };
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                key.ColumnFamily = "c";
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("10", "c", "50", "d"))))
                {
                    var c = 10;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        Assert.AreEqual("c", cell.Key.ColumnFamily);

                        Assert.IsTrue(scanner.Next(out cell));
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        Assert.AreEqual("d", cell.Key.ColumnFamily);
                        ++c;
                    }

                    Assert.AreEqual(51, c);
                }
            }
        }

        /// <summary>
        ///     The same interval once the start family also carries a qualified column: the scan
        ///     returns the unqualified cell before the qualified one.
        /// </summary>
        [TestMethod]
        public void CellIntervalWithAQualifiedColumn()
        {
            using (var _table = EnsureTable("CellInterval", Schema))
            {
                var key = new Key { ColumnFamily = "d" };
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                key.ColumnFamily = "c";
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                key.ColumnQualifier = "1";
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("10", "c", "50", "d"))))
                {
                    var c = 10;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        Assert.AreEqual("c", cell.Key.ColumnFamily);
                        Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);

                        Assert.IsTrue(scanner.Next(out cell));
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        Assert.AreEqual("c", cell.Key.ColumnFamily);
                        Assert.AreEqual("1", cell.Key.ColumnQualifier);

                        Assert.IsTrue(scanner.Next(out cell));
                        Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                        Assert.AreEqual("d", cell.Key.ColumnFamily);
                        Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                        ++c;
                    }

                    Assert.AreEqual(51, c);
                }
            }
        }

        /// <summary>
        ///     The order cells come back in when the rows at the ends of the interval carry cells
        ///     in five families. The interval bounds name two of them, and everything between
        ///     comes back in family order.
        /// </summary>
        [TestMethod]
        public void CellIntervalOrderAcrossFamilies()
        {
            using (var _table = EnsureTable("CellInterval", Schema))
            {
                var key = new Key { ColumnFamily = "d" };
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                key.ColumnFamily = "c";
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                key.ColumnQualifier = "1";
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                key.ColumnQualifier = null;
                using (var mutator = _table.CreateMutator())
                {
                    key.Row = "10";
                    key.ColumnFamily = "a";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                    key.ColumnFamily = "b";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                    key.ColumnFamily = "e";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                    key.Row = "11";
                    key.ColumnFamily = "a";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                    key.ColumnFamily = "b";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                    key.ColumnFamily = "e";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("10", "c", "11", "d"))))
                {
                    Cell cell;
                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("10", cell.Key.Row);
                    Assert.AreEqual("c", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual(10, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("10", cell.Key.Row);
                    Assert.AreEqual("c", cell.Key.ColumnFamily);
                    Assert.AreEqual("1", cell.Key.ColumnQualifier);
                    Assert.AreEqual(10, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("10", cell.Key.Row);
                    Assert.AreEqual("d", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual(10, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("10", cell.Key.Row);
                    Assert.AreEqual("e", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual("10e", Encoding.GetString(cell.Value));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("11a", Encoding.GetString(cell.Value));
                    Assert.AreEqual("a", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("b", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual("11b", Encoding.GetString(cell.Value));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("c", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual(11, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("c", cell.Key.ColumnFamily);
                    Assert.AreEqual("1", cell.Key.ColumnQualifier);
                    Assert.AreEqual(11, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("d", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual(11, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsFalse(scanner.Next(out cell));
                }
            }
        }

        /// <summary>
        ///     Bounds that name a column qualifier as well as a family, inclusive and exclusive,
        ///     and one narrowed further by an explicit column.
        /// </summary>
        [TestMethod]
        public void CellIntervalWithQualifiedBounds()
        {
            using (var _table = EnsureTable("CellInterval", Schema))
            {
                var key = new Key { ColumnFamily = "d" };
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                key.ColumnFamily = "c";
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                key.ColumnQualifier = "1";
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, BitConverter.GetBytes(i));
                    }
                }

                key.ColumnQualifier = null;
                using (var mutator = _table.CreateMutator())
                {
                    key.Row = "10";
                    key.ColumnFamily = "a";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                    key.ColumnFamily = "b";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                    key.ColumnFamily = "e";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                    key.Row = "11";
                    key.ColumnFamily = "a";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                    key.ColumnFamily = "b";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                    key.ColumnFamily = "e";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily));
                }

                key.ColumnQualifier = "1";
                using (var mutator = _table.CreateMutator())
                {
                    key.Row = "11";
                    key.ColumnFamily = "d";
                    mutator.Set(key, Encoding.GetBytes(key.Row + key.ColumnFamily + key.ColumnQualifier));
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("10", "c", "1", "11", "d", "1"))))
                {
                    Cell cell;
                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("10", cell.Key.Row);
                    Assert.AreEqual("c", cell.Key.ColumnFamily);
                    Assert.AreEqual("1", cell.Key.ColumnQualifier);
                    Assert.AreEqual(10, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("10", cell.Key.Row);
                    Assert.AreEqual("d", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual(10, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("10", cell.Key.Row);
                    Assert.AreEqual("e", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual("10e", Encoding.GetString(cell.Value));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("a", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual("11a", Encoding.GetString(cell.Value));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("b", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual("11b", Encoding.GetString(cell.Value));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("c", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual(11, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("c", cell.Key.ColumnFamily);
                    Assert.AreEqual("1", cell.Key.ColumnQualifier);
                    Assert.AreEqual(11, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("d", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual(11, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("d", cell.Key.ColumnFamily);
                    Assert.AreEqual("1", cell.Key.ColumnQualifier);
                    Assert.AreEqual("11d1", Encoding.GetString(cell.Value));

                    Assert.IsFalse(scanner.Next(out cell));
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("10", "c", "1", false, "11", "d", "1", false))))
                {
                    Cell cell;
                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("10", cell.Key.Row);
                    Assert.AreEqual("d", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual(10, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("10", cell.Key.Row);
                    Assert.AreEqual("e", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual("10e", Encoding.GetString(cell.Value));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("a", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual("11a", Encoding.GetString(cell.Value));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("b", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual("11b", Encoding.GetString(cell.Value));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("c", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual(11, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("c", cell.Key.ColumnFamily);
                    Assert.AreEqual("1", cell.Key.ColumnQualifier);
                    Assert.AreEqual(11, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("d", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual(11, BitConverter.ToInt32(cell.Value, 0));

                    Assert.IsFalse(scanner.Next(out cell));
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("10", "c", "1", false, "11", "d", null, false))))
                {
                    Cell cell;
                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual(10, BitConverter.ToInt32(cell.Value, 0));
                    Assert.AreEqual("d", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("10e", Encoding.GetString(cell.Value));
                    Assert.AreEqual("e", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11a", Encoding.GetString(cell.Value));
                    Assert.AreEqual("a", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11b", Encoding.GetString(cell.Value));
                    Assert.AreEqual("b", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual(11, BitConverter.ToInt32(cell.Value, 0));
                    Assert.AreEqual("c", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual(11, BitConverter.ToInt32(cell.Value, 0));
                    Assert.AreEqual("c", cell.Key.ColumnFamily);
                    Assert.AreEqual("1", cell.Key.ColumnQualifier);

                    Assert.IsFalse(scanner.Next(out cell));
                }

                using (var scanner = _table.CreateScanner(new ScanSpec(new CellInterval("10", "c", "1", "11", "d", "1")).AddColumn("a", "b", "e")))
                {
                    Cell cell;
                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("10", cell.Key.Row);
                    Assert.AreEqual("e", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual("10e", Encoding.GetString(cell.Value));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("a", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual("11a", Encoding.GetString(cell.Value));

                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("11", cell.Key.Row);
                    Assert.AreEqual("b", cell.Key.ColumnFamily);
                    Assert.AreEqual(string.Empty, cell.Key.ColumnQualifier);
                    Assert.AreEqual("11b", Encoding.GetString(cell.Value));

                    Assert.IsFalse(scanner.Next(out cell));
                }
            }
        }

        [TestMethod]
        public void CellOffset()
        {
            const int CellOffset = CountB;

            using (var scanner = table.CreateScanner(new ScanSpec { CellOffset = CellOffset }))
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountA + CountB + CountC - CellOffset, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { CellOffset = CellOffset }.AddColumn("a")))
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountA - CellOffset, c);
            }

            string[] rows = { "AA", "BB", "CC", "DD" };
            string[] columnFamilies = { "d", "e", "f", "g" };
            string[] columnQualifiers = { "0", "1", "2", "3" };

            foreach (var row in rows)
            {
                using (var mutator = table.CreateMutator())
                {
                    foreach (var columnFamily in columnFamilies)
                    {
                        foreach (var columnQualifier in columnQualifiers)
                        {
                            mutator.Set(new Key(row, columnFamily, columnQualifier), Encoding.GetBytes(row + columnFamily + columnQualifier));
                        }
                    }
                }
            }

            using (
                var scanner =
                    table.CreateScanner(new ScanSpec { CellOffset = rows.Length * columnFamilies.Length * columnQualifiers.Length / 2, ScanAndFilter = true }.AddRow(rows)))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(rows.Length * columnFamilies.Length * columnQualifiers.Length / 2, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { CellOffset = 1 }.AddRowInterval(new RowInterval("AA", "BB"), new RowInterval("CC", "DD"))))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(rows.Length * columnFamilies.Length * columnQualifiers.Length - 2, c); // CellOffset applies to each row interval
            }
        }

        [TestMethod]
        public void ColumnFamily()
        {
            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("a")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountA, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("b")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountB, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("c")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountC, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("a", "b")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountA + CountB, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("a", "c")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountA + CountC, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("b", "c")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountB + CountC, c);
            }
        }

        [TestMethod]
        public void ColumnQualifier()
        {
            string[] columnQualifiers = { null, "1", "2", "3" };

            var key = new Key { Row = Guid.NewGuid().ToString(), ColumnFamily = "d" };
            using (var mutator = table.CreateMutator())
            {
                foreach (var columnQualifier in columnQualifiers)
                {
                    key.ColumnQualifier = columnQualifier;
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }

                key = new Key { Row = Guid.NewGuid().ToString(), ColumnFamily = "d" };
                foreach (var columnQualifier in columnQualifiers)
                {
                    key.ColumnQualifier = columnQualifier;
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(2 * columnQualifiers.Length, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec(key.Row).AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    Assert.AreEqual(key.Row, cell.Key.Row);
                    ++c;
                }

                Assert.AreEqual(columnQualifiers.Length, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d:")))
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

            using (var scanner = table.CreateScanner(new ScanSpec(key.Row).AddColumn("d:")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    Assert.AreEqual(key.Row, cell.Key.Row);
                    ++c;
                }

                Assert.AreEqual(1, c);
            }

            foreach (var columnQualifier in columnQualifiers)
            {
                using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d:" + (!string.IsNullOrEmpty(columnQualifier) ? columnQualifier : string.Empty))))
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

            foreach (var columnQualifier in columnQualifiers)
            {
                using (var scanner = table.CreateScanner(new ScanSpec().AddCell(key.Row, "d", !string.IsNullOrEmpty(columnQualifier) ? columnQualifier : string.Empty)))
                {
                    var c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                        Assert.AreEqual(key.Row, cell.Key.Row);
                        ++c;
                    }

                    Assert.AreEqual(1, c);
                }
            }
        }

        [TestMethod]
        public void ColumnQualifierPrefix()
        {
            var key = new Key { ColumnFamily = "d" };
            using (var mutator = table.CreateMutator())
            {
                for (var i = 0; i < 100; ++i)
                {
                    key.Row = i.ToString("D2");
                    key.ColumnQualifier = (100 + i / 10).ToString("D3") + (1000 + i).ToString("D4");
                    mutator.Set(key, Encoding.GetBytes((1000 + i).ToString("D4")));
                }
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d:^1")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(100, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d:^10")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(100, c);
            }

            for (var i = 0; i < 10; ++i)
            {
                using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d:^" + (100 + i).ToString("D3"))))
                {
                    var c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual((1000 + 10 * i + c).ToString("D4"), Encoding.GetString(cell.Value));
                        ++c;
                    }

                    Assert.AreEqual(10, c);
                }
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d:^10510")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(10, c);
            }
        }

        [TestMethod]
        public void ColumnQualifierRegex()
        {
            var key = new Key { ColumnFamily = "d" };
            using (var mutator = table.CreateMutator())
            {
                for (var i = 0; i < 100; ++i)
                {
                    key.Row = i.ToString("D2");
                    key.ColumnQualifier = (100 + i / 10).ToString("D3");
                    mutator.Set(key, Encoding.GetBytes((1000 + i).ToString("D4")));
                }
            }

            for (var i = 0; i < 10; ++i)
            {
                using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d:" + (100 + i).ToString("D3"))))
                {
                    var c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual((1000 + 10 * i + c).ToString("D4"), Encoding.GetString(cell.Value));
                        ++c;
                    }

                    Assert.AreEqual(10, c);
                }
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d:/10[56]/")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(20, c);
            }
        }

        [TestMethod]
        public void ColumnQualifierIndex()
        {
            const string ScanTableValueIndexSchema =
                "<Schema><AccessGroup name=\"default\" blksz=\"1024\">" +
                "<ColumnFamily><Name>a</Name><QualifierIndex>true</QualifierIndex></ColumnFamily>" +
                "<ColumnFamily><Name>b</Name><QualifierIndex>true</QualifierIndex></ColumnFamily>" +
                "<ColumnFamily><Name>c</Name><QualifierIndex>true</QualifierIndex></ColumnFamily>" +
                "</AccessGroup></Schema>";

            const int Count = 10000;
            using (var _table = EnsureTable("ColumnQualifierIndex", ScanTableValueIndexSchema))
            {
                var key = new Key();
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < Count; ++i)
                    {
                        var qualifier = i.ToString();
                        var value = Encoding.GetBytes(qualifier);

                        key.ColumnFamily = "a";
                        key.ColumnQualifier = qualifier;
                        key.Row = null;
                        mutator.Set(key, value);

                        key.ColumnFamily = "b";
                        key.ColumnQualifier = qualifier;
                        key.Row = null;
                        mutator.Set(key, value);

                        key.ColumnFamily = "c";
                        key.ColumnQualifier = qualifier;
                        key.Row = null;
                        mutator.Set(key, value);
                    }
                }

                string[] columnFamilies = { "a", "b", "c" };
                var rng = new Random();
                for (var i = 0; i < Count / 100; ++i)
                {
                    var qualifier = rng.Next(Count).ToString();
                    var scanSpec = new ScanSpec().AddColumn(columnFamilies[rng.Next(columnFamilies.Length)] + ":" + qualifier);
                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        Cell cell;
                        Assert.IsTrue(scanner.Next(out cell));
                        Assert.AreEqual(qualifier, Encoding.GetString(cell.Value));
                        Assert.IsFalse(scanner.Next(out cell));
                    }
                }

                int[] ranges = { 1000, 300, 56, 9999, 789, 4 };
                int[] occurrence = { 1, 11, 111, 1, 11, 1111 };
                Assert.AreEqual(ranges.Length, occurrence.Length);
                for (var i = 0; i < ranges.Length; ++i)
                {
                    var qualifier = ranges[i].ToString();
                    var scanSpec = new ScanSpec().AddColumn(columnFamilies[rng.Next(columnFamilies.Length)] + ":^" + qualifier);
                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        var c = 0;
                        var cell = new Cell();
                        while (scanner.Move(cell))
                        {
                            Assert.IsTrue(Encoding.GetString(cell.Value).StartsWith(qualifier));
                            ++c;
                        }

                        Assert.AreEqual(occurrence[i], c, $"range = {ranges[i]}");
                    }
                }
            }
        }

        [TestMethod]
        public void MaxCells()
        {
            string[] rows = { "A", "B", "C", "D" };
            string[] columnFamilies = { "d", "e", "f", "g" };
            string[] columnQualifiers = { "0", "1", "2", "3" };

            foreach (var row in rows)
            {
                using (var mutator = table.CreateMutator())
                {
                    foreach (var columnFamily in columnFamilies)
                    {
                        foreach (var columnQualifier in columnQualifiers)
                        {
                            mutator.Set(new Key(row, columnFamily, columnQualifier), Encoding.GetBytes(row + columnFamily + columnQualifier));
                        }
                    }
                }
            }

            using (var scanner = table.CreateScanner(new ScanSpec { MaxCells = 5 }))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(5, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { MaxCellsColumnFamily = 2 }.AddRow(rows)))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(rows.Length * columnFamilies.Length * 2, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { MaxCellsColumnFamily = 2, ScanAndFilter = true }.AddRow(rows)))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(rows.Length * columnFamilies.Length * 2, c);
            }

            foreach (var columnFamily in columnFamilies)
            {
                using (var scanner = table.CreateScanner(new ScanSpec { MaxCells = 3, ScanAndFilter = true }.AddRow(rows).AddColumn(columnFamily)))
                {
                    var c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        ++c;
                    }

                    Assert.AreEqual(3, c);
                }
            }

            using (var scanner = table.CreateScanner(new ScanSpec { MaxCells = 5 }.AddRowInterval(new RowInterval("A", "B"), new RowInterval("C", "D"))))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(2 * 5, c); // Applies to each row interval individual
            }

            using (var scanner = table.CreateScanner(new ScanSpec { MaxCellsColumnFamily = 2 }.AddRowInterval(new RowInterval("A", "B"), new RowInterval("C", "D"))))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(rows.Length * columnFamilies.Length * 2, c);
            }
        }

        /// <summary>
        ///     A scan spec built from a random set of cells, ten times over with a different set
        ///     and a different size each time.
        /// </summary>
        [TestMethod]
        public void RandomCellSets()
        {
            var random = new Random();
            const string Cf = "abcdefg";
            const int Count = 10000;
            using (var _table = EnsureTable("RandomCells", Schema))
            {
                var keys = new List<Key>(Count);
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < Count; ++i)
                    {
                        var key = new Key
                        {
                            Row = Guid.NewGuid().ToString(),
                            ColumnFamily = new string(new[] { Cf[random.Next(Cf.Length)] }),
                            ColumnQualifier = random.Next(Cf.Length).ToString(CultureInfo.InvariantCulture)
                        };
                        keys.Add(key);
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                    }
                }


                for (var r = 0; r < 10; ++r)
                {
                    var countCells = 10 + random.Next(Count - 10);
                    var scanSpec = new ScanSpec();
                    foreach (var k in Shuffle(keys))
                    {
                        scanSpec.AddCell(k);
                        if (scanSpec.CellCount == countCells)
                        {
                            break;
                        }
                    }

                    var comparer = new KeyComparer(false);
                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        Assert.AreSame(scanSpec, scanner.ScanSpec);
                        var cell = new Cell();
                        var c = 0;
                        while (scanner.Move(cell))
                        {
                            Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                            Assert.IsTrue(comparer.Equals(scanSpec.Cells[c++], cell.Key));
                        }

                        Assert.AreEqual(scanSpec.CellCount, c);
                    }
                }
            }
        }

        /// <summary>
        ///     The same with a random set of rows rather than of cells.
        /// </summary>
        [TestMethod]
        public void RandomRowSets()
        {
            var random = new Random();
            const string Cf = "abcdefg";
            const int Count = 10000;
            using (var _table = EnsureTable("RandomCells", Schema))
            {
                var keys = new List<Key>(Count);
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < Count; ++i)
                    {
                        var key = new Key
                        {
                            Row = Guid.NewGuid().ToString(),
                            ColumnFamily = new string(new[] { Cf[random.Next(Cf.Length)] }),
                            ColumnQualifier = random.Next(Cf.Length).ToString(CultureInfo.InvariantCulture)
                        };
                        keys.Add(key);
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                    }
                }


                for (var r = 0; r < 10; ++r)
                {
                    var countCells = 10 + random.Next(Count - 10);
                    var scanSpec = new ScanSpec();
                    foreach (var k in Shuffle(keys))
                    {
                        scanSpec.AddRow(k.Row);
                        if (scanSpec.RowCount == countCells)
                        {
                            break;
                        }
                    }

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        Assert.AreSame(scanSpec, scanner.ScanSpec);
                        var cell = new Cell();
                        var c = 0;
                        while (scanner.Move(cell))
                        {
                            Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                            Assert.AreEqual(scanSpec.Rows[c++], cell.Key.Row);
                        }

                        Assert.AreEqual(scanSpec.RowCount, c);
                    }
                }
            }
        }

        /// <summary>
        ///     A random row set in a scan spec declared sorted, which tells the provider it need
        ///     not order the keys itself.
        /// </summary>
        [TestMethod]
        public void RandomRowSetsDeclaredSorted()
        {
            var random = new Random();
            const string Cf = "abcdefg";
            const int Count = 10000;
            using (var _table = EnsureTable("RandomCells", Schema))
            {
                var keys = new List<Key>(Count);
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < Count; ++i)
                    {
                        var key = new Key
                        {
                            Row = Guid.NewGuid().ToString(),
                            ColumnFamily = new string(new[] { Cf[random.Next(Cf.Length)] }),
                            ColumnQualifier = random.Next(Cf.Length).ToString(CultureInfo.InvariantCulture)
                        };
                        keys.Add(key);
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                    }
                }


                for (var r = 0; r < 10; ++r)
                {
                    var rows = new HashSet<string>();
                    var countCells = 10 + random.Next(Count - 10);
                    var scanSpec = new ScanSpec(true);
                    foreach (var k in Shuffle(keys))
                    {
                        scanSpec.AddRow(k.Row);
                        rows.Add(k.Row);
                        if (scanSpec.RowCount == countCells)
                        {
                            break;
                        }
                    }

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        Assert.AreSame(scanSpec, scanner.ScanSpec);
                        var cell = new Cell();
                        var c = 0;
                        while (scanner.Move(cell))
                        {
                            Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                            Assert.IsTrue(rows.Contains(cell.Key.Row));
                            ++c;
                        }

                        Assert.AreEqual(scanSpec.RowCount, c);
                    }
                }
            }
        }

        /// <summary>
        ///     A random row set narrowed to a single column family and qualifier, with
        ///     ScanAndFilter set.
        /// </summary>
        [TestMethod]
        public void RandomRowsOneColumn()
        {
            var random = new Random();
            const string Cf = "abcdefg";
            const int Count = 10000;
            using (var _table = EnsureTable("RandomCells", Schema))
            {
                var keys = new List<Key>(Count);
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < Count; ++i)
                    {
                        var key = new Key
                        {
                            Row = Guid.NewGuid().ToString(),
                            ColumnFamily = new string(new[] { Cf[random.Next(Cf.Length)] }),
                            ColumnQualifier = random.Next(Cf.Length).ToString(CultureInfo.InvariantCulture)
                        };
                        keys.Add(key);
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                    }
                }


                for (var r = 0; r < 10; ++r)
                {
                    var rows = new HashSet<string>();
                    var columnFamily = new string(new[] { Cf[random.Next(Cf.Length)] });
                    var columnQualifier = random.Next(Cf.Length).ToString(CultureInfo.InvariantCulture);
                    var countCells = 10 + random.Next(Count / 10);
                    var scanSpec = new ScanSpec { ScanAndFilter = true };
                    foreach (var k in Shuffle(keys).Where(k => k.ColumnFamily == columnFamily && k.ColumnQualifier == columnQualifier))
                    {
                        Assert.AreEqual(columnFamily, k.ColumnFamily);
                        Assert.AreEqual(columnQualifier, k.ColumnQualifier);
                        scanSpec.AddColumn(k.ColumnFamily + ":" + k.ColumnQualifier);
                        scanSpec.AddRow(k.Row);
                        rows.Add(k.Row);
                        if (scanSpec.RowCount == countCells)
                        {
                            break;
                        }
                    }

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        Assert.AreSame(scanSpec, scanner.ScanSpec);
                        var cell = new Cell();
                        var c = 0;
                        while (scanner.Move(cell))
                        {
                            Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                            Assert.AreEqual(columnFamily, cell.Key.ColumnFamily);
                            Assert.AreEqual(columnQualifier, cell.Key.ColumnQualifier);
                            Assert.IsTrue(rows.Contains(cell.Key.Row));
                            ++c;
                        }

                        Assert.AreEqual(scanSpec.RowCount, c);
                    }
                }
            }
        }

        /// <summary>
        ///     A random row set narrowed to one qualifier across every family.
        /// </summary>
        [TestMethod]
        public void RandomRowsOneQualifier()
        {
            var random = new Random();
            const string Cf = "abcdefg";
            const int Count = 10000;
            using (var _table = EnsureTable("RandomCells", Schema))
            {
                var keys = new List<Key>(Count);
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < Count; ++i)
                    {
                        var key = new Key
                        {
                            Row = Guid.NewGuid().ToString(),
                            ColumnFamily = new string(new[] { Cf[random.Next(Cf.Length)] }),
                            ColumnQualifier = random.Next(Cf.Length).ToString(CultureInfo.InvariantCulture)
                        };
                        keys.Add(key);
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                    }
                }


                for (var r = 0; r < 10; ++r)
                {
                    var rows = new HashSet<string>();
                    var columnQualifier = random.Next(Cf.Length).ToString(CultureInfo.InvariantCulture);
                    var countCells = 10 + random.Next(Count / 10);
                    var scanSpec = new ScanSpec { ScanAndFilter = true };
                    foreach (var k in Shuffle(keys).Where(k => k.ColumnQualifier == columnQualifier))
                    {
                        Assert.AreEqual(columnQualifier, k.ColumnQualifier);
                        scanSpec.AddColumn(k.ColumnFamily + ":" + k.ColumnQualifier);
                        scanSpec.AddRow(k.Row);
                        rows.Add(k.Row);
                        if (scanSpec.RowCount == countCells)
                        {
                            break;
                        }
                    }

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        Assert.AreSame(scanSpec, scanner.ScanSpec);
                        var cell = new Cell();
                        var c = 0;
                        while (scanner.Move(cell))
                        {
                            Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                            Assert.AreEqual(columnQualifier, cell.Key.ColumnQualifier);
                            Assert.IsTrue(rows.Contains(cell.Key.Row));
                            ++c;
                        }

                        Assert.AreEqual(scanSpec.RowCount, c);
                    }
                }
            }
        }

        /// <summary>
        ///     Named rows and explicitly listed columns rather than random ones, so the cell count
        ///     is known in advance and can be asserted against the spec's own row count.
        /// </summary>
        [TestMethod]
        public void ExplicitColumnsAndRows()
        {
            var random = new Random();
            const string Cf = "abcdefg";
            const int Count = 10000;
            using (var _table = EnsureTable("RandomCells", Schema))
            {
                var keys = new List<Key>(Count);
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < Count; ++i)
                    {
                        var key = new Key
                        {
                            Row = Guid.NewGuid().ToString(),
                            ColumnFamily = new string(new[] { Cf[random.Next(Cf.Length)] }),
                            ColumnQualifier = random.Next(Cf.Length).ToString(CultureInfo.InvariantCulture)
                        };
                        keys.Add(key);
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                    }
                }


                using (var mutator = _table.CreateMutator())
                {
                    var key = new Key { Row = "A", ColumnFamily = "a", ColumnQualifier = "1" };
                    mutator.Set(key, Encoding.GetBytes(key.Row));

                    key = new Key { Row = "B", ColumnFamily = "a", ColumnQualifier = "2" };
                    mutator.Set(key, Encoding.GetBytes(key.Row));

                    key = new Key { Row = "C", ColumnFamily = "c", ColumnQualifier = "3" };
                    mutator.Set(key, Encoding.GetBytes(key.Row));

                    key = new Key { Row = "D", ColumnFamily = "c", ColumnQualifier = "4" };
                    mutator.Set(key, Encoding.GetBytes(key.Row));

                    key = new Key { Row = "E", ColumnFamily = "b", ColumnQualifier = "5" };
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }

                {
                    var scanSpec = new ScanSpec { ScanAndFilter = true };
                    scanSpec.AddColumn("a:1");
                    scanSpec.AddColumn("a:2");
                    scanSpec.AddColumn("b:5");
                    scanSpec.AddColumn("c:3");
                    scanSpec.AddColumn("c:4");
                    scanSpec.AddRow("A");
                    scanSpec.AddRow("B");

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        Assert.AreSame(scanSpec, scanner.ScanSpec);
                        var cell = new Cell();
                        var c = 0;
                        while (scanner.Move(cell))
                        {
                            Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                            ++c;
                        }

                        Assert.AreEqual(scanSpec.RowCount, c);
                    }

                    scanSpec = new ScanSpec { ScanAndFilter = true };
                    scanSpec.AddColumn("a", "b:5", "c");
                    scanSpec.AddRow("A");
                    scanSpec.AddRow("C");
                    scanSpec.AddRow("E");

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        Assert.AreSame(scanSpec, scanner.ScanSpec);
                        var cell = new Cell();
                        var c = 0;
                        while (scanner.Move(cell))
                        {
                            Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                            ++c;
                        }

                        Assert.AreEqual(scanSpec.RowCount, c);
                    }
                }
            }
        }
    }
}
