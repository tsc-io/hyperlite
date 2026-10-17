namespace Hyperlite.Test
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Scanning a whole table, and what a scanner does at the edges of one.
    /// </summary>
    /// <remarks>
    ///     The table is this fixture's own, seeded in ClassInitialize: CountA rows in family a,
    ///     the first CountB of them also in b and the first CountC also in c. Families d to g are
    ///     scratch - a case may write what it needs into them, and TestInitialize empties them
    ///     again, which is why a case may assume they start empty.
    /// </remarks>
    [TestClass]
    public sealed class TestTableScannerBasics : TableScannerFixture
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            CreateAndSeedTable(typeof(TestTableScannerBasics));
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

        [TestMethod]
        public void WholeTable()
        {
            using (var scanner = table.CreateScanner())
            {
                Assert.IsNull(scanner.ScanSpec);
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountA + CountB + CountC, c);
            }
        }

        [TestMethod]
        public void AbandonedPartWayThrough()
        {
            using (var scanner = table.CreateScanner())
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    if (c == CountA)
                    {
                        break;
                    }

                    ++c;
                }

                Assert.AreEqual(CountA, c);
            }
        }

        [TestMethod]
        public void KeysOnly()
        {
            using (var scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }))
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.IsFalse(string.IsNullOrEmpty(cell.Key.Row));
                    Assert.IsNull(cell.Value);
                    ++c;
                }

                Assert.AreEqual(CountA + CountB + CountC, c);
            }
        }

        /// <summary>
        /// A scanner which has reported the end of the scan keeps reporting it.
        /// </summary>
        /// <remarks>
        /// Repeated enumeration is what IEnumerator promises, and it costs nothing to honour as
        /// long as the reader remembers that it is done. An embedded provider must remember: a
        /// statement stepped to the end restarts when it is stepped again, so an exhausted scanner
        /// would hand out the whole result a second time. The scan specs below pick a different
        /// reader in the sqlite provider - everything, a row set, row intervals, cell intervals -
        /// because the readers remember separately.
        /// </remarks>
        [TestMethod]
        public void PastTheEnd()
        {
            using (var _table = EnsureTable("PastTheEnd", Schema))
            {
                var key = new Key { ColumnFamily = "a" };
                using (var mutator = _table.CreateMutator())
                {
                    for (var i = 0; i < 10; ++i)
                    {
                        key.Row = i.ToString("D2");
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                    }
                }

                var scanSpecs = new[]
                {
                    new ScanSpec(),
                    new ScanSpec { ScanAndFilter = true }.AddRow("01").AddRow("03").AddRow("05"),
                    new ScanSpec()
                        .AddRowInterval(new RowInterval("01", true, "02", true))
                        .AddRowInterval(new RowInterval("07", true, "08", true)),
                    new ScanSpec().AddCellInterval(new CellInterval("01", "a", "02", "a"))
                };

                foreach (var scanSpec in scanSpecs)
                {
                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        var c = 0;
                        Cell cell;
                        while (scanner.Next(out cell))
                        {
                            ++c;
                        }

                        Assert.IsTrue(c > 0);

                        for (var i = 0; i < 3; ++i)
                        {
                            Assert.IsFalse(scanner.Next(out cell), "Next past the end of the scan");
                        }
                    }

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        var cell = new Cell();
                        while (scanner.Move(cell))
                        {
                        }

                        for (var i = 0; i < 3; ++i)
                        {
                            Assert.IsFalse(scanner.Move(cell), "Move past the end of the scan");
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void FromSeveralThreads()
        {
            var t1 = new Thread(
                () =>
                    {
                        using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("a")))
                        {
                            Thread.Sleep(100);

                            var c = 0;
                            Cell cell;
                            while (scanner.Next(out cell))
                            {
                                Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                                ++c;
                            }

                            Assert.AreEqual(CountA, c);
                        }
                    });

            var t2 = new Thread(
                () =>
                    {
                        using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("b")))
                        {
                            Thread.Sleep(50);

                            var c = 0;
                            Cell cell;
                            while (scanner.Next(out cell))
                            {
                                Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                                ++c;
                            }

                            Assert.AreEqual(CountB, c);
                        }
                    });

            var t3 = new Thread(
                () =>
                    {
                        using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("c")))
                        {
                            Thread.Sleep(25);

                            var c = 0;
                            Cell cell;
                            while (scanner.Next(out cell))
                            {
                                Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                                ++c;
                            }

                            Assert.AreEqual(CountC, c);
                        }
                    });

            t1.Start();
            t2.Start();
            t3.Start();
            t1.Join();
            t2.Join();
            t3.Join();

            using (var _table = EnsureTable("FromSeveralThreads", Schema))
            {
                var action = new Action<string>(
                    cf =>
                    {
                        using (var t = Ns.OpenTable("FromSeveralThreads"))
                        {
                            var key = new Key { ColumnFamily = cf };
                            using (var mutator = t.CreateMutator())
                            {
                                for (var i = 0; i < 10000; ++i)
                                {
                                    key.Row = i.ToString("D6");
                                    mutator.Set(key, BitConverter.GetBytes(i));
                                }
                            }

                            using (var scanner = t.CreateScanner(new ScanSpec().AddColumn(cf)))
                            {
                                var c = 0;
                                Cell cell;
                                while (scanner.Next(out cell))
                                {
                                    Assert.AreEqual(c.ToString("D6"), cell.Key.Row);
                                    Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                                    ++c;
                                }

                                Assert.AreEqual(10000, c);
                            }
                        }
                    });

                t1 = new Thread(() => action("a"));
                t2 = new Thread(() => action("b"));
                t3 = new Thread(() => action("c"));
                t1.Start();
                t2.Start();
                t3.Start();
                t1.Join();
                t2.Join();
                t3.Join();
            }
        }
    }
}
