namespace Hyperlite.Test
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Choosing rows: an interval, an offset, a regular expression, a limit, and an
    ///     explicit set of keys.
    /// </summary>
    /// <remarks>
    ///     The table is this fixture's own, seeded in ClassInitialize: CountA rows in family a,
    ///     the first CountB of them also in b and the first CountC also in c. Families d to g are
    ///     scratch - a case may write what it needs into them, and TestInitialize empties them
    ///     again, which is why a case may assume they start empty.
    /// </remarks>
    [TestClass]
    public sealed class TestTableScannerRowSelection : TableScannerFixture
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            CreateAndSeedTable(typeof(TestTableScannerRowSelection));
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
        public void RowInterval()
        {
            var key = new Key { ColumnFamily = "d" };
            using (var mutator = table.CreateMutator())
            {
                for (var i = 0; i < 100; ++i)
                {
                    key.Row = i.ToString("D2");
                    mutator.Set(key, BitConverter.GetBytes(i));
                }
            }

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval(null, "50")).AddColumn("d")))
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

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval(null, false, "50", false)).AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                    ++c;
                }

                Assert.AreEqual(50, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval(string.Empty, "50")).AddColumn("d")))
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

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval(string.Empty, false, "50", false)).AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                    ++c;
                }

                Assert.AreEqual(50, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval("90", null)).AddColumn("d")))
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

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval("90", false, null, false)).AddColumn("d")))
            {
                var c = 91;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                    ++c;
                }

                Assert.AreEqual(100, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval("90", string.Empty)).AddColumn("d")))
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

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval("90", false, string.Empty, false)).AddColumn("d")))
            {
                var c = 91;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(c, BitConverter.ToInt32(cell.Value, 0));
                    ++c;
                }

                Assert.AreEqual(100, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval("10", "50")).AddColumn("d")))
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

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval("10", true, "50", false)).AddColumn("d")))
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

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval("10", false, "50", false)).AddColumn("d")))
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

            using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval("10", "20")).AddRowInterval(new RowInterval("40", "50")).AddColumn("d")))
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

        [TestMethod]
        public void RowOffset()
        {
            var rowKeys = new List<string>(CountA);
            using (var scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }.AddColumn("a")))
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    rowKeys.Add(cell.Key.Row);
                }

                Assert.AreEqual(CountA, rowKeys.Count);
            }

            const int RowOffset = CountB;
            var countB = 0;
            var countC = 0;
            using (var scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }.AddColumn("b", "c").AddRowInterval(new RowInterval(rowKeys[RowOffset], null))))
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    if (cell.Key.ColumnFamily == "b")
                    {
                        ++countB;
                    }
                    else if (cell.Key.ColumnFamily == "c")
                    {
                        ++countC;
                    }
                    else
                    {
                        Assert.Fail();
                    }
                }
            }

            using (var scanner = table.CreateScanner(new ScanSpec { RowOffset = RowOffset }))
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountA - RowOffset + countB + countC, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { RowOffset = RowOffset }))
            {
                var ca = 0;
                var cb = 0;
                var cc = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    if (cell.Key.ColumnFamily == "a")
                    {
                        ++ca;
                    }
                    else if (cell.Key.ColumnFamily == "b")
                    {
                        ++cb;
                    }
                    else if (cell.Key.ColumnFamily == "c")
                    {
                        ++cc;
                    }
                    else
                    {
                        Assert.Fail();
                    }
                }

                Assert.AreEqual(CountA - RowOffset, ca);
                Assert.AreEqual(countB, cb);
                Assert.AreEqual(countC, cc);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { RowOffset = RowOffset }.AddColumn("a")))
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountA - RowOffset, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { RowOffset = RowOffset }.AddColumn("a").AddColumn("b")))
            {
                var ca = 0;
                var cb = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    if (cell.Key.ColumnFamily == "a")
                    {
                        ++ca;
                    }
                    else if (cell.Key.ColumnFamily == "b")
                    {
                        ++cb;
                    }
                    else
                    {
                        Assert.Fail();
                    }
                }

                Assert.AreEqual(CountA - RowOffset, ca);
                Assert.AreEqual(countB, cb);
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

            using (var scanner = table.CreateScanner(new ScanSpec { RowOffset = rows.Length / 2, ScanAndFilter = true }.AddRow(rows)))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(rows.Length / 2 * columnFamilies.Length * columnQualifiers.Length, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { RowOffset = 1 }.AddRowInterval(new RowInterval("AA", "BB"), new RowInterval("CC", "DD"))))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(2 * columnFamilies.Length * columnQualifiers.Length, c); // RowOffset applies to each row interval
            }
        }

        [TestMethod]
        public void RowRegex()
        {
            var key = new Key { Row = "AAA", ColumnFamily = "d" };
            using (var mutator = table.CreateMutator())
            {
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "AAB";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "ABB";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.Row = "BBB";
                mutator.Set(key, Encoding.GetBytes(key.Row));
            }

            using (var scanner = table.CreateScanner(new ScanSpec { RowRegex = "A.*" }.AddColumn("d")))
            {
                var c = 0;
                foreach (var cell in scanner)
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(3, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { RowRegex = "AA." }.AddColumn("d")))
            {
                var c = 0;
                foreach (var cell in scanner)
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { RowRegex = "AB.*" }.AddColumn("d")))
            {
                var c = 0;
                foreach (var cell in scanner)
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { RowRegex = "^B.*" }.AddColumn("d")))
            {
                var c = 0;
                foreach (var cell in scanner)
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(1, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { RowRegex = "^BB.*" }.AddColumn("d")))
            {
                var c = 0;
                foreach (var cell in scanner)
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(1, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { RowRegex = "^B.." }.AddColumn("d")))
            {
                var c = 0;
                foreach (var cell in scanner)
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(1, c);
            }
        }

        [TestMethod]
        public void MaxRows()
        {
            using (var scanner = table.CreateScanner(new ScanSpec { MaxRows = CountC }.AddColumn("a")))
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountC, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { MaxRows = CountB }.AddColumn("a")))
            {
                var c = 0;
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(CountB, c);
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

            using (var scanner = table.CreateScanner(new ScanSpec { MaxRows = rows.Length / 2, ScanAndFilter = true }.AddRow(rows)))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(rows.Length / 2 * columnFamilies.Length * columnQualifiers.Length, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { MaxRows = rows.Length / 2 }.AddRowInterval(new RowInterval("AA", "BB"), new RowInterval("CC", "DD")))
                )
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(2 * rows.Length / 2 * columnFamilies.Length * columnQualifiers.Length, c); // MaxRows applies to each row interval individual
            }
        }

        [TestMethod]
        public void RandomRows()
        {
            var rowKeys = new List<string>();
            var cell = new Cell();
            ITableScanner scanner;
            var random = new Random();

            while (rowKeys.Count < R)
            {
                using (scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }.AddColumn("a")))
                {
                    while (scanner.Move(cell))
                    {
                        Assert.IsNull(cell.Value);
                        if ((random.Next() % 3) == 0)
                        {
                            rowKeys.Add(cell.Key.Row);
                            if (rowKeys.Count == R)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            var scanSpec1 = new ScanSpec().AddColumn("a");
            var scanSpec2 = new ScanSpec();
            foreach (var t in rowKeys)
            {
                var rowKey = rowKeys[random.Next(rowKeys.Count)];
                scanSpec1.AddRow(rowKey);
                scanSpec2.AddCell(rowKey, "a", null);
            }

            Assert.AreEqual(R, scanSpec1.RowCount);
            Assert.AreEqual(R, scanSpec2.Cells.Count);
            var c = 0;
            using (scanner = table.CreateScanner(scanSpec1))
            {
                Assert.AreSame(scanSpec1, scanner.ScanSpec);
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    Assert.IsTrue(c < scanSpec1.RowCount);
                    Assert.AreEqual(scanSpec1.Rows[c], cell.Key.Row);
                    ++c;
                }
            }

            Assert.AreEqual(R, c);

            c = 0;
            using (scanner = table.CreateScanner(scanSpec2))
            {
                Assert.AreSame(scanSpec2, scanner.ScanSpec);
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    Assert.IsTrue(c < scanSpec2.Cells.Count);
                    Assert.AreEqual(scanSpec2.Cells[c].Row, cell.Key.Row);
                    ++c;
                }
            }

            Assert.AreEqual(R, c);
        }

        [TestMethod]
        public void RandomRowsOneScannerEach()
        {
            var rowKeys = new List<string>();
            var cell = new Cell();
            ITableScanner scanner;
            var random = new Random();

            while (rowKeys.Count < R)
            {
                using (scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }.AddColumn("a")))
                {
                    while (scanner.Move(cell))
                    {
                        Assert.IsNull(cell.Value);
                        if ((random.Next() % 3) == 0)
                        {
                            rowKeys.Add(cell.Key.Row);
                            if (rowKeys.Count == R)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            Assert.AreEqual(R, rowKeys.Count);
            var c = 0;
            foreach (var t in rowKeys)
            {
                using (scanner = table.CreateScanner(new ScanSpec(new Key(rowKeys[random.Next(rowKeys.Count)], "a", null))))
                {
                    while (scanner.Move(cell))
                    {
                        Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                        ++c;
                    }
                }
            }

            Assert.AreEqual(R, c);
        }

        [TestMethod]
        public void RandomRowsScanAndFilter()
        {
            this.RandomRowsScanAndFilter(1);
            this.RandomRowsScanAndFilter(2);
            this.RandomRowsScanAndFilter(3);
            this.RandomRowsScanAndFilter(R);
        }

        private void RandomRowsScanAndFilter(int count)
        {
            var cell = new Cell();
            ITableScanner scanner;
            var random = new Random();
            var scanSpec = new ScanSpec { ScanAndFilter = true }.AddColumn("a");

            while (scanSpec.RowCount < count)
            {
                using (scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }.AddColumn("a")))
                {
                    while (scanner.Move(cell))
                    {
                        Assert.IsNull(cell.Value);
                        if ((random.Next() % 3) == 0)
                        {
                            scanSpec.AddRow(cell.Key.Row);
                            if (scanSpec.RowCount == count)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            Assert.AreEqual(count, scanSpec.RowCount);

            var c = 0;
            var previousRow = string.Empty;
            using (scanner = table.CreateScanner(scanSpec))
            {
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    Assert.IsTrue(c < scanSpec.RowCount);
                    Assert.IsTrue(string.Compare(previousRow, cell.Key.Row) < 0);
                    previousRow = cell.Key.Row;
                    ++c;
                }
            }

            Assert.AreEqual(count, c);
        }

        [TestMethod]
        public void RandomRowsSortedRowKeys()
        {
            var cell = new Cell();
            ITableScanner scanner;
            var random = new Random();
            var scanSpec1 = new ScanSpec(true).AddColumn("a");
            var scanSpec2 = new ScanSpec(true);

            while (scanSpec1.RowCount < R)
            {
                using (scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }.AddColumn("a")))
                {
                    while (scanner.Move(cell))
                    {
                        Assert.IsNull(cell.Value);
                        if ((random.Next() % 3) == 0)
                        {
                            scanSpec1.AddRow(cell.Key.Row);
                            scanSpec2.AddCell(cell.Key.Row, "a", null);
                            if (scanSpec1.RowCount == R)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            Assert.AreEqual(R, scanSpec1.RowCount);
            Assert.AreEqual(R, scanSpec2.Cells.Count);

            var c = 0;
            var previousRow = string.Empty;
            using (scanner = table.CreateScanner(scanSpec1))
            {
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    Assert.IsTrue(c < scanSpec1.RowCount);
                    Assert.AreEqual(scanSpec1.Rows[c], cell.Key.Row);
                    Assert.IsTrue(string.Compare(previousRow, cell.Key.Row) < 0);
                    previousRow = cell.Key.Row;
                    ++c;
                }
            }

            Assert.AreEqual(R, c);

            c = 0;
            previousRow = string.Empty;
            using (scanner = table.CreateScanner(scanSpec2))
            {
                while (scanner.Move(cell))
                {
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    Assert.IsTrue(c < scanSpec2.Cells.Count);
                    Assert.AreEqual(scanSpec2.Cells[c].Row, cell.Key.Row);
                    Assert.IsTrue(string.Compare(previousRow, cell.Key.Row) < 0);
                    previousRow = cell.Key.Row;
                    ++c;
                }
            }

            Assert.AreEqual(R, c);
        }
    }
}
