namespace Hyperlite.Test
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Two clients against one database at the same time.
    /// </summary>
    [TestClass]
    [Ignore] // TODO
    public sealed class TestMultipleInstances : TestBase
    {
        private const int CountA = 100000;

        private const int CountB = 10000;

        private const int CountC = 1000;

        private const string UriB = "localhost"; // TODO, check also with 2nd host different to localhost

        private static readonly UTF8Encoding Encoding = new UTF8Encoding();

        private static IClient clientB;

        private static IContext contextB;

        private static INamespace nsB;

        private static ITable tableA;

        private static ITable tableB;

        [ClassCleanup]
        public static void ClassCleanup()
        {
            tableB.Dispose();
            nsB.Dispose();
            clientB.DropNamespace(NsName, DropDispositions.Complete);
            clientB.Dispose();
            contextB.Dispose();

            tableA.Dispose();
        }

        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            const string Schema =
                "<Schema><AccessGroup name=\"default\" blksz=\"1024\">" +
                "<ColumnFamily><Name>a</Name></ColumnFamily>" +
                "<ColumnFamily><Name>b</Name></ColumnFamily>" +
                "<ColumnFamily><Name>c</Name></ColumnFamily>" +
                "</AccessGroup></Schema>";

            tableA = EnsureTable(typeof(TestMultipleInstances), Schema);

            var properties = new Dictionary<string, object> { { "Uri", UriB } };

            contextB = Hyperlite.Context.Create(ConnectionString, properties);
            clientB = contextB.CreateClient();
            nsB = clientB.OpenNamespace(NsName, OpenDispositions.OpenAlways);
            nsB.DropTable(typeof(TestMultipleInstances).Name, DropDispositions.IfExists);
            nsB.CreateTable(typeof(TestMultipleInstances).Name, Schema);
            tableB = nsB.OpenTable(typeof(TestMultipleInstances).Name);
        }

        [TestMethod]
        public void Copy()
        {
            var key = new Key { ColumnFamily = "a" };
            using (var mutator = tableA.CreateMutator())
            {
                for (var n = 0; n < CountC; ++n)
                {
                    key.Row = "A" + Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            key = new Key { ColumnFamily = "b" };
            using (var mutator = tableB.CreateMutator())
            {
                for (var n = 0; n < CountC; ++n)
                {
                    key.Row = "B" + Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            using (var scanner = tableA.CreateScanner(new ScanSpec().AddColumn("a")))
            using (var mutator = tableB.CreateMutator())
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("A"));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    mutator.Set(cell);
                    ++c;
                }

                Assert.AreEqual(CountC, c);
            }

            using (var scanner = tableB.CreateScanner(new ScanSpec().AddColumn("b")))
            using (var mutator = tableA.CreateMutator())
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("B"));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    mutator.Set(cell);
                    ++c;
                }

                Assert.AreEqual(CountC, c);
            }

            using (var scanner = tableA.CreateScanner(new ScanSpec().AddColumn("a")))
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("A"));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountC, c);
            }

            using (var scanner = tableA.CreateScanner(new ScanSpec().AddColumn("b")))
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("B"));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountC, c);
            }

            using (var scanner = tableB.CreateScanner(new ScanSpec().AddColumn("a")))
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("A"));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountC, c);
            }

            using (var scanner = tableB.CreateScanner(new ScanSpec().AddColumn("b")))
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("B"));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountC, c);
            }

            using (var scannerA = tableA.CreateScanner(new ScanSpec().AddColumn("a")))
            using (var scannerB = tableB.CreateScanner(new ScanSpec().AddColumn("a")))
            {
                var c = 0;
                var cellA = new Cell();
                var cellB = new Cell();
                while (scannerA.Move(cellA))
                {
                    Assert.IsTrue(scannerB.Move(cellB));
                    Assert.IsTrue(cellA.Key.Row.StartsWith("A"));
                    Assert.AreEqual(cellA.Key.Row, Encoding.GetString(cellA.Value));
                    Assert.IsTrue(cellB.Key.Row.StartsWith("A"));
                    Assert.AreEqual(cellB.Key.Row, Encoding.GetString(cellB.Value));
                    Assert.AreEqual(cellA.Key, cellB.Key);
                    ++c;
                }

                Assert.IsFalse(scannerB.Move(cellB));
                Assert.AreEqual(CountC, c);
            }

            using (var scannerB = tableA.CreateScanner(new ScanSpec().AddColumn("b")))
            using (var scannerA = tableB.CreateScanner(new ScanSpec().AddColumn("b")))
            {
                var c = 0;
                var cellB = new Cell();
                var cellA = new Cell();
                while (scannerB.Move(cellB))
                {
                    Assert.IsTrue(scannerA.Move(cellA));
                    Assert.IsTrue(cellA.Key.Row.StartsWith("B"));
                    Assert.AreEqual(cellA.Key.Row, Encoding.GetString(cellA.Value));
                    Assert.IsTrue(cellB.Key.Row.StartsWith("B"));
                    Assert.AreEqual(cellB.Key.Row, Encoding.GetString(cellB.Value));
                    Assert.AreEqual(cellA.Key, cellB.Key);
                    ++c;
                }

                Assert.IsFalse(scannerA.Move(cellB));
                Assert.AreEqual(CountC, c);
            }
        }

        [TestMethod]
        public void Delete()
        {
            var key = new Key { ColumnFamily = "a" };
            using (var mutator = tableA.CreateMutator())
            {
                for (var n = 0; n < CountC; ++n)
                {
                    key.Row = "A" + Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            key = new Key { ColumnFamily = "b" };
            using (var mutator = tableB.CreateMutator())
            {
                for (var n = 0; n < CountC; ++n)
                {
                    key.Row = "B" + Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            using (var scanner = tableA.CreateScanner(new ScanSpec { KeysOnly = true }))
            {
                using (var mutator = tableA.CreateMutator())
                {
                    var cell = new Cell();
                    while (scanner.Move(cell))
                    {
                        mutator.Delete(cell.Key);
                    }
                }
            }

            using (var scanner = tableA.CreateScanner())
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("A"));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(0, c);
            }

            using (var scanner = tableB.CreateScanner(new ScanSpec { KeysOnly = true }))
            {
                using (var mutator = tableB.CreateMutator())
                {
                    var cell = new Cell();
                    while (scanner.Move(cell))
                    {
                        mutator.Delete(cell.Key);
                    }
                }
            }

            using (var scanner = tableB.CreateScanner())
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("B"));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(0, c);
            }
        }

        [TestMethod]
        public void Set()
        {
            var key = new Key { ColumnFamily = "a" };
            using (var mutator = tableA.CreateMutator())
            {
                for (var n = 0; n < CountC; ++n)
                {
                    key.Row = "A" + Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            key = new Key { ColumnFamily = "b" };
            using (var mutator = tableB.CreateMutator())
            {
                for (var n = 0; n < CountC; ++n)
                {
                    key.Row = "B" + Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            using (var scanner = tableA.CreateScanner())
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("A"));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountC, c);
            }

            using (var scanner = tableB.CreateScanner())
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.IsTrue(cell.Key.Row.StartsWith("B"));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountC, c);
            }
        }

        [TestInitialize]
        public void TestInitialize()
        {
            TestBase.ContinueExecution();
            Delete(tableA);
            Delete(tableB);
        }

        private static int GetCellCount(ITable _table)
        {
            var c = 0;
            using (var scanner = _table.CreateScanner())
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    ++c;
                }
            }

            return c;
        }

        private static void InitializeTableData(ITable _table)
        {
            var key = new Key();
            using (var mutator = _table.CreateMutator(MutatorSpec.CreateChunked()))
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
