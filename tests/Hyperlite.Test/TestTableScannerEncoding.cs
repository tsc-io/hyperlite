namespace Hyperlite.Test
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Row keys and values that are not plain ascii.
    /// </summary>
    /// <remarks>
    ///     Both cases write through the api and read back through it, so what they pin is that
    ///     the bytes survive the round trip rather than any particular encoding on disk.
    ///     <para>
    ///     The table is this fixture's own, seeded in ClassInitialize: CountA rows in family a,
    ///     the first CountB of them also in b and the first CountC also in c. Families d to g are
    ///     scratch - a case may write what it needs into them, and TestInitialize empties them
    ///     again, which is why a case may assume they start empty.
    ///     </para>
    /// </remarks>
    [TestClass]
    public sealed class TestTableScannerEncoding : TableScannerFixture
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            CreateAndSeedTable(typeof(TestTableScannerEncoding));
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
        public void SpecialCharacters()
        {
            var specialCharacters = new[]
                {
                    '~',
                    '!',
                    '@',
                    '#',
                    '$',
                    '%',
                    '^',
                    '&',
                    '*',
                    '(',
                    ')',
                    '{',
                    '}',
                    '\\',
                    '"',
                    '\'',
                    ',',
                    ';',
                    ':',
                    '.',
                    '/',
                    '?',
                    'Ü',
                    'Ä',
                    'Ö'
                };

            var escapeCharacters = new[]
                {
                    '"',
                    '\'',
                    '\\',
                    '/'
                };

            var rows = new List<string>();
            var key = new Key { ColumnFamily = "d" };

            using (var mutator = table.CreateMutator())
            {
                foreach (var c in specialCharacters)
                {
                    key.Row = c.ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                    rows.Add(key.Row);

                    foreach (var e in escapeCharacters)
                    {
                        key.Row = e.ToString() + c.ToString();
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                        rows.Add(key.Row);

                        key.Row = e.ToString() + c.ToString() + e.ToString();
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                        rows.Add(key.Row);
                    }
                }
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("d")))
            {
                var c = 0;
                Cell cell;
                while (scanner.Next(out cell))
                {
                    Assert.IsTrue(rows.Contains(cell.Key.Row), $"row {cell.Key.Row}");
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    ++c;
                }

                Assert.AreEqual(rows.Count, c);
            }

            foreach (var row in Shuffle(rows))
            {
                using (var scanner = table.CreateScanner(new ScanSpec(row).AddColumn("d")))
                {
                    Cell cell;
                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    Assert.IsFalse(scanner.Next(out cell));
                }
            }

            foreach (var row in Shuffle(rows))
            {
                using (var scanner = table.CreateScanner(new ScanSpec(row) { ScanAndFilter = true }.AddColumn("d")))
                {
                    Cell cell;
                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    Assert.IsFalse(scanner.Next(out cell));
                }
            }

            for (int i = 0; i < 10; ++i)
            {
                var rowsScanAndFilter = new HashSet<string>();
                foreach (var row in Shuffle(rows))
                {
                    rowsScanAndFilter.Add(row);
                    if (rowsScanAndFilter.Count >= 3)
                    {
                        break;
                    }
                }

                using (var scanner = table.CreateScanner(new ScanSpec { ScanAndFilter = true }.AddColumn("d").AddRow(rowsScanAndFilter)))
                {
                    var c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                        ++c;

                        Assert.IsTrue(rowsScanAndFilter.Contains(cell.Key.Row));
                    }

                    Assert.AreEqual(rowsScanAndFilter.Count, c);
                }
            }
        }

        [TestMethod]
        public void UnicodeCharacters()
        {
            var rows = new[]
            {
                "프1000_1",
                "1프1000_",
                "_1000_1프"
            };

            var key = new Key { ColumnFamily = "d" };
            using (var mutator = table.CreateMutator())
            {
                foreach (var row in rows)
                {
                    key.Row = row;
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

                    Assert.IsTrue(rows.Contains(cell.Key.Row));
                }

                Assert.AreEqual(rows.Length, c);
            }

            foreach (var row in Shuffle(rows))
            {
                using (var scanner = table.CreateScanner(new ScanSpec(row).AddColumn("d")))
                {
                    Cell cell;
                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    Assert.IsFalse(scanner.Next(out cell));
                }
            }

            foreach (var row in Shuffle(rows))
            {
                using (var scanner = table.CreateScanner(new ScanSpec(row) { ScanAndFilter = true }.AddColumn("d")))
                {
                    Cell cell;
                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                    Assert.IsFalse(scanner.Next(out cell));
                }
            }

            for (int i = 0; i < 10; ++i)
            {
                var rowsScanAndFilter = new HashSet<string>();
                foreach (var row in Shuffle(rows))
                {
                    rowsScanAndFilter.Add(row);
                    if (rowsScanAndFilter.Count >= 3)
                    {
                        break;
                    }
                }

                using (var scanner = table.CreateScanner(new ScanSpec { ScanAndFilter = true }.AddColumn("d").AddRow(rowsScanAndFilter)))
                {
                    var c = 0;
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                        ++c;

                        Assert.IsTrue(rowsScanAndFilter.Contains(cell.Key.Row));
                    }

                    Assert.AreEqual(rowsScanAndFilter.Count, c);
                }
            }
        }
    }
}
