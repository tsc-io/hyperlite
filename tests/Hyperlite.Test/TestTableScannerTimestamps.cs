namespace Hyperlite.Test
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Time: timestamp intervals, the direction a scan walks them, and how many revisions
    ///     of a cell come back.
    /// </summary>
    /// <remarks>
    ///     The table is this fixture's own, seeded in ClassInitialize: CountA rows in family a,
    ///     the first CountB of them also in b and the first CountC also in c. Families d to g are
    ///     scratch - a case may write what it needs into them, and TestInitialize empties them
    ///     again, which is why a case may assume they start empty.
    /// </remarks>
    [TestClass]
    public sealed class TestTableScannerTimestamps : TableScannerFixture
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            CreateAndSeedTable(typeof(TestTableScannerTimestamps));
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
        public void DateTimeInterval()
        {
            string[] items = { "0", "1", "2", "3" };
            var dateTimeSet = new DateTime[items.Length + 1];

            const int Wait = 100;
            var c = 0;
            var key = new Key { ColumnFamily = "d" };
            using (var mutator = table.CreateMutator())
            {
                foreach (var item in items)
                {
                    key.Row = Guid.NewGuid().ToString();
                    dateTimeSet[c++] = key.DateTime = DateTime.UtcNow;
                    mutator.Set(key, Encoding.GetBytes(item));
                    Thread.Sleep(Wait);
                }
            }

            dateTimeSet[c] = DateTime.UtcNow;

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d")))
            {
                c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.IsTrue(cell.Key.DateTime >= dateTimeSet[0]);
                    Assert.IsTrue(cell.Key.DateTime < dateTimeSet[dateTimeSet.Length - 1]);
                    ++c;
                }

                Assert.AreEqual(items.Length, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { StartDateTime = dateTimeSet[0], EndDateTime = dateTimeSet[dateTimeSet.Length - 1] }.AddColumn("d")))
            {
                c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(items.Length, c);
            }

            for (var n = 0; n < items.Length; ++n)
            {
                using (
                    var scanner =
                        table.CreateScanner(new ScanSpec { StartDateTime = dateTimeSet[n], EndDateTime = dateTimeSet[n] + TimeSpan.FromMilliseconds(Wait / 2) }.AddColumn("d")))
                {
                    c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        ++c;
                    }

                    Assert.AreEqual(1, c);
                }
            }
        }

        [TestMethod]
        public void DateTimeIntervalDescending()
        {
            const string SchemaWithTimeOrder =
                "<Schema><AccessGroup name=\"default\">" +
                "<ColumnFamily><Name>desc</Name><TimeOrder>DESC</TimeOrder></ColumnFamily>" +
                "</AccessGroup></Schema>";

            using (var _table = EnsureTable("DateTimeIntervalDescending", SchemaWithTimeOrder))
            {
                string[] items = { "0", "1", "2", "3" };
                var dateTimeSet = new DateTime[items.Length + 1];

                const int Wait = 100;
                var c = 0;
                var key = new Key("row") { ColumnFamily = "desc" };
                using (var mutator = _table.CreateMutator())
                {
                    foreach (var item in items)
                    {
                        dateTimeSet[c++] = key.DateTime = DateTime.UtcNow;
                        mutator.Set(key, Encoding.GetBytes(item));
                        Thread.Sleep(Wait);
                    }
                }

                dateTimeSet[c] = DateTime.UtcNow;

                using (var scanner = _table.CreateScanner())
                {
                    var prevDateTime = new DateTime(0);
                    c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.IsTrue(cell.Key.DateTime > prevDateTime);
                        prevDateTime = cell.Key.DateTime;
                        ++c;
                    }

                    Assert.AreEqual(items.Length, c);
                }

                var scanSpec = ScanSpecBuilder
                    .Create()
                        .WithColumns("desc")
                        .WithRows()
                    .Build();

                using (var scanner = _table.CreateScanner(scanSpec))
                {
                    var prevDateTime = new DateTime(0);
                    c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.IsTrue(cell.Key.DateTime > prevDateTime);
                        prevDateTime = cell.Key.DateTime;
                        ++c;
                    }

                    Assert.AreEqual(items.Length, c);
                }

                scanSpec = ScanSpecBuilder
                    .Create()
                        .WithColumns("desc")
                        .WithRows()
                        .StartDateTime(dateTimeSet[0])
                        .EndDateTime(dateTimeSet[dateTimeSet.Length - 1])
                    .Build();

                using (var scanner = _table.CreateScanner(scanSpec))
                {
                    c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        ++c;
                    }

                    Assert.AreEqual(items.Length, c);
                }

                for (var n = 0; n < items.Length; ++n)
                {
                    scanSpec = ScanSpecBuilder
                        .Create()
                            .WithColumns("desc")
                            .WithRows()
                            .StartDateTime(dateTimeSet[n])
                            .EndDateTime(dateTimeSet[n] + TimeSpan.FromMilliseconds(Wait / 2))
                        .Build();

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        c = 0;
                        Cell cell;
                        while (scanner.Next(out cell))
                        {
                            ++c;
                        }

                        Assert.AreEqual(1, c);
                    }
                }
            }
        }

        [TestMethod]
        public void DateTimeIntervalBothDirections()
        {
            const string SchemaWithTimeOrder =
                "<Schema><AccessGroup name=\"default\">" +
                "<ColumnFamily><Name>asc</Name><TimeOrder>ASC</TimeOrder></ColumnFamily>" +
                "<ColumnFamily><Name>desc</Name><TimeOrder>DESC</TimeOrder></ColumnFamily>" +
                "</AccessGroup></Schema>";

            using (var _table = EnsureTable("DateTimeIntervalBothDirections", SchemaWithTimeOrder))
            {
                string[] items = { "0", "1", "2", "3" };
                var dateTimeSet = new DateTime[items.Length + 1];

                const int Wait = 100;
                var c = 0;
                var key = new Key("row");
                using (var mutator = _table.CreateMutator())
                {
                    foreach (var item in items)
                    {
                        dateTimeSet[c++] = key.DateTime = DateTime.UtcNow;
                        key.ColumnFamily = "asc";
                        mutator.Set(key, Encoding.GetBytes(item));
                        key.ColumnFamily = "desc";
                        mutator.Set(key, Encoding.GetBytes(item));
                        Thread.Sleep(Wait);
                    }
                }

                dateTimeSet[c] = DateTime.UtcNow;

                // Ascending
                var scanSpec = ScanSpecBuilder
                    .Create()
                        .WithColumns("asc")
                        .WithRows()
                    .Build();

                using (var scanner = _table.CreateScanner(scanSpec))
                {
                    var prevDateTime = DateTime.UtcNow;
                    c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.IsTrue(cell.Key.DateTime < prevDateTime);
                        prevDateTime = cell.Key.DateTime;
                        ++c;
                    }

                    Assert.AreEqual(items.Length, c);
                }

                scanSpec = ScanSpecBuilder
                    .Create()
                        .WithColumns("asc")
                        .WithRows()
                        .StartDateTime(dateTimeSet[0])
                        .EndDateTime(dateTimeSet[dateTimeSet.Length - 1])
                    .Build();

                using (var scanner = _table.CreateScanner(scanSpec))
                {
                    c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        ++c;
                    }

                    Assert.AreEqual(items.Length, c);
                }

                for (var n = 0; n < items.Length; ++n)
                {
                    scanSpec = ScanSpecBuilder
                        .Create()
                            .WithColumns("asc")
                            .WithRows()
                            .StartDateTime(dateTimeSet[n])
                            .EndDateTime(dateTimeSet[n] + TimeSpan.FromMilliseconds(Wait / 2))
                        .Build();

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        c = 0;
                        Cell cell;
                        while (scanner.Next(out cell))
                        {
                            ++c;
                        }

                        Assert.AreEqual(1, c);
                    }
                }

                // Descending
                scanSpec = ScanSpecBuilder
                    .Create()
                        .WithColumns("desc")
                        .WithRows()
                    .Build();

                using (var scanner = _table.CreateScanner(scanSpec))
                {
                    var prevDateTime = new DateTime(0);
                    c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.IsTrue(cell.Key.DateTime > prevDateTime);
                        prevDateTime = cell.Key.DateTime;
                        ++c;
                    }

                    Assert.AreEqual(items.Length, c);
                }

                scanSpec = ScanSpecBuilder
                    .Create()
                        .WithColumns("desc")
                        .WithRows()
                        .StartDateTime(dateTimeSet[0])
                        .EndDateTime(dateTimeSet[dateTimeSet.Length - 1])
                    .Build();

                using (var scanner = _table.CreateScanner(scanSpec))
                {
                    c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        ++c;
                    }

                    Assert.AreEqual(items.Length, c);
                }

                for (var n = 0; n < items.Length; ++n)
                {
                    scanSpec = ScanSpecBuilder
                        .Create()
                            .WithColumns("desc")
                            .WithRows()
                            .StartDateTime(dateTimeSet[n])
                            .EndDateTime(dateTimeSet[n] + TimeSpan.FromMilliseconds(Wait / 2))
                        .Build();

                    using (var scanner = _table.CreateScanner(scanSpec))
                    {
                        c = 0;
                        Cell cell;
                        while (scanner.Next(out cell))
                        {
                            ++c;
                        }

                        Assert.AreEqual(1, c);
                    }
                }

                // Mixed
                scanSpec = ScanSpecBuilder
                    .Create()
                        .WithColumns("asc", "desc")
                        .WithRows()
                        .StartDateTime(dateTimeSet[1])
                        .EndDateTime(dateTimeSet[dateTimeSet.Length - 3])
                    .Build();

                using (var scanner = _table.CreateScanner(scanSpec))
                {
                    c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.IsTrue(cell.Key.DateTime >= scanSpec.StartDateTime);
                        Assert.IsTrue(cell.Key.DateTime <= scanSpec.EndDateTime);
                        var value = Encoding.GetString(cell.Value);
                        Assert.IsTrue(value != items[0] && value != items[items.Length - 1]);
                        ++c;
                    }

                    Assert.AreEqual(2 * (items.Length - 2), c);
                }
            }
        }

        [TestMethod]
        public void VersionLimit()
        {
            string[] rows = { "A", "B", "C", "D" };
            string[] versions = { "0", "1", "2", "3" };

            foreach (var row in rows)
            {
                var key = new Key { Row = row, ColumnFamily = "d" };
                using (var mutator = table.CreateMutator())
                {
                    foreach (var version in versions)
                    {
                        mutator.Set(key, Encoding.GetBytes(version));
                    }
                }
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(rows[c / versions.Length], cell.Key.Row);
                    Assert.AreEqual(versions[versions.Length - (c % versions.Length) - 1], Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(rows.Length * versions.Length, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { MaxVersions = 1 }.AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(rows[c], cell.Key.Row);
                    Assert.AreEqual(versions[versions.Length - 1], Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(rows.Length, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { MaxVersions = 2 }.AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual(rows[c / 2], cell.Key.Row);
                    Assert.AreEqual(versions[versions.Length - (c % 2) - 1], Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(rows.Length * 2, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { MaxVersions = 1 }.AddColumn("d").AddRow("A", "B")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { ScanAndFilter = true, MaxVersions = 1 }.AddColumn("d").AddRow("A", "B")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(2, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { MaxVersions = 2 }.AddColumn("d").AddRow("B", "C")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(4, c);
            }

            using (var scanner = table.CreateScanner(new ScanSpec { ScanAndFilter = true, MaxVersions = 2 }.AddColumn("d").AddRow("B", "C")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    ++c;
                }

                Assert.AreEqual(4, c);
            }
        }
    }
}
