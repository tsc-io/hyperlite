namespace Hyperlite.Test
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Deleting: a row, a cell, and a set of them at once.
    /// </summary>
    /// <remarks>
    ///     Each scenario below is written once, as a private method taking a
    ///     <see cref="MutatorSpec" />, and run by four tests: with no spec, chunked, queued, and
    ///     chunked and queued together. A scenario that passes unbuffered and fails chunked is
    ///     the interesting case, and naming the four separately is what makes the report say
    ///     which buffering mode broke.
    /// </remarks>
    [TestClass]
    public sealed class TestTableMutatorDelete : TableMutatorFixture
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            CreateTable(typeof(TestTableMutatorDelete));
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

            // every scenario asserts an empty table on entry
            Delete(table);
        }

        [TestMethod]
        public void Delete()
        {
            this.Delete(null);
        }

        [TestMethod]
        public void DeleteChunked()
        {
            this.Delete(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void DeleteChunkedQueued()
        {
            this.Delete(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void DeleteQueued()
        {
            this.Delete(MutatorSpec.CreateQueued());
        }

        [TestMethod]
        public void DeleteSet()
        {
            this.DeleteSet(null);
        }

        [TestMethod]
        public void DeleteSetChunked()
        {
            this.DeleteSet(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void DeleteSetChunkedQueued()
        {
            this.DeleteSet(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void DeleteSetQueued()
        {
            this.DeleteSet(MutatorSpec.CreateQueued());
        }

        private void Delete(MutatorSpec mutatorSpec)
        {
            var key = new Key { ColumnFamily = "a" };

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                key.Row = "Row does not exist";
                mutator.Set(key, Encoding.GetBytes(key.Row));
            }

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                for (var n = 0; n < Count; ++n)
                {
                    key.Row = Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            var rowKeys = new SortedSet<string>();
            using (var scanner = table.CreateScanner())
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    rowKeys.Add(cell.Key.Row);
                }
            }

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                foreach (var rowKey in rowKeys)
                {
                    mutator.Delete(rowKey);
                }
            }

            Assert.AreEqual(0, this.GetCellCount());

            key = new Key { ColumnFamily = "a" };
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                key.Row = "Row does not exist";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                for (var n = 0; n < Count; ++n)
                {
                    key.Row = Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            using (var scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }))
            using (var mutator = table.CreateMutator())
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    mutator.Delete(cell.Key);
                }
            }

            Assert.AreEqual(0, this.GetCellCount());

            key = new Key { ColumnFamily = "a" };
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                for (var n = 0; n < Count; ++n)
                {
                    key.Row = Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            rowKeys = new SortedSet<string>();
            using (var scanner = table.CreateScanner())
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    rowKeys.Add(cell.Key.Row);
                }
            }

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                key = new Key();
                foreach (var rowKey in rowKeys)
                {
                    key.Row = rowKey;
                    mutator.Delete(key);
                }
            }

            Assert.AreEqual(0, this.GetCellCount());

            key = new Key { ColumnFamily = "a" };
            var keys = new List<Key>();
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                for (var n = 0; n < Count; ++n)
                {
                    key.Row = Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                    keys.Add((Key)key.Clone());
                }
            }

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(keys);
            }

            Assert.AreEqual(0, this.GetCellCount());

            key = new Key { ColumnFamily = "a", ColumnQualifier = null };
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                for (var n = 0; n < Count; ++n)
                {
                    key.Row = Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            var cells = new List<Cell>();
            using (var scanner = table.CreateScanner())
            {
                Cell cell;
                while (scanner.Next(out cell))
                {
                    cells.Add(cell);
                }
            }

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(cells);
            }

            Assert.AreEqual(0, this.GetCellCount());

            key = new Key { ColumnFamily = "a", ColumnQualifier = string.Empty };
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                for (var n = 0; n < Count; ++n)
                {
                    key.Row = Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            cells = new List<Cell>();
            using (var scanner = table.CreateScanner())
            {
                Cell cell;
                while (scanner.Next(out cell))
                {
                    cells.Add(cell);
                }
            }

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(cells);
            }

            Assert.AreEqual(0, this.GetCellCount());

            key = new Key();
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                key.Row = Guid.NewGuid().ToString();
                key.ColumnFamily = "a";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.ColumnFamily = "b";
                mutator.Set(key, Encoding.GetBytes(key.Row));
            }

            Assert.AreEqual(2, this.GetCellCount());

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(key);
            }

            using (var scanner = table.CreateScanner())
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.AreEqual("a", cell.Key.ColumnFamily);
                }
            }

            Assert.AreEqual(1, this.GetCellCount());

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                key.ColumnFamily = "b";
                key.ColumnQualifier = "1";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.ColumnQualifier = "2";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.ColumnQualifier = "3";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.ColumnQualifier = string.Empty;
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.ColumnQualifier = "4";
                mutator.Set(key, Encoding.GetBytes(key.Row));
            }

            Assert.AreEqual(6, this.GetCellCount());

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(key);
            }

            Assert.AreEqual(5, this.GetCellCount());

            key.ColumnQualifier = "1";
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(key);
            }

            Assert.AreEqual(4, this.GetCellCount());

            key.ColumnQualifier = string.Empty;
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(key);
            }

            Assert.AreEqual(3, this.GetCellCount());

            key.ColumnQualifier = null;
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(key);
            }

            using (var scanner = table.CreateScanner())
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    Assert.AreEqual("a", cell.Key.ColumnFamily);
                }
            }

            Assert.AreEqual(1, this.GetCellCount());

            key.ColumnFamily = string.Empty; // usually use null
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(key);
            }

            Assert.AreEqual(0, this.GetCellCount());

            key = new Key(); // FIXME(remove this line), issue 'delete with unspecified timestamp applying to all future inserts'

            var dateTimes = new List<DateTime>();
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                key.ColumnFamily = "b";
                key.ColumnQualifier = "1";
                key.DateTime = DateTime.UtcNow;
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                Assert.IsTrue(dateTimes[dateTimes.Count - 1] < key.DateTime);
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                Assert.IsTrue(dateTimes[dateTimes.Count - 1] < key.DateTime);
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                Assert.IsTrue(dateTimes[dateTimes.Count - 1] < key.DateTime);
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                Assert.IsTrue(dateTimes[dateTimes.Count - 1] < key.DateTime);
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                Assert.IsTrue(dateTimes[dateTimes.Count - 1] < key.DateTime);
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
            }

            Assert.AreEqual(6, dateTimes.Count);
            Assert.AreEqual(6, this.GetCellCount());

            key.DateTime = dateTimes[1];
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(key);
            }

            Assert.AreEqual(4, this.GetCellCount());

            key.ColumnQualifier = null;
            key.DateTime = dateTimes[3];
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(key);
            }

            Assert.AreEqual(2, this.GetCellCount());

            key.ColumnFamily = null;
            key.ColumnQualifier = null;
            key.DateTime = dateTimes[4];
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(key);
            }

            Assert.AreEqual(1, this.GetCellCount());

            key.ColumnFamily = null;
            key.ColumnQualifier = null;
            key.DateTime = dateTimes[5];
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Delete(key);
            }

            Assert.AreEqual(0, this.GetCellCount());
        }

        private void DeleteSet(MutatorSpec mutatorSpec)
        {
            Cell cell;
            var key = new Key();
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                for (var n = 0; n < Count; ++n)
                {
                    key.Row = Guid.NewGuid().ToString();
                    key.ColumnFamily = "a";
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                    key.ColumnFamily = "b";
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            var cells = new List<Cell>();
            using (var scanner = table.CreateScanner())
            {
                while (scanner.Next(out cell))
                {
                    cells.Add(new Cell(new Key(cell.Key.Row), CellFlag.DeleteRow));
                }
            }

            Assert.AreEqual(2 * Count, cells.Count);

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cells);
            }

            Assert.AreEqual(0, this.GetCellCount());

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                for (var n = 0; n < Count; ++n)
                {
                    key.Row = Guid.NewGuid().ToString();
                    key.ColumnFamily = "a";
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                    key.ColumnFamily = "b";
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            cells.Clear();
            using (var scanner = table.CreateScanner())
            {
                while (scanner.Next(out cell))
                {
                    cells.Add(new Cell(cell.Key, CellFlag.DeleteRow));
                }
            }

            Assert.AreEqual(2 * Count, cells.Count);

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cells);
            }

            Assert.AreEqual(0, this.GetCellCount());

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                for (var n = 0; n < Count; ++n)
                {
                    key.Row = Guid.NewGuid().ToString();
                    key.ColumnFamily = "a";
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                    key.ColumnFamily = "b";
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            cells.Clear();
            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("b")))
            {
                while (scanner.Next(out cell))
                {
                    cells.Add(new Cell(cell.Key, CellFlag.DeleteColumnFamily));
                }
            }

            Assert.AreEqual(Count, cells.Count);

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cells);
            }

            Assert.AreEqual(Count, this.GetCellCount());

            cells.Clear();
            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("a")))
            {
                while (scanner.Next(out cell))
                {
                    cells.Add(new Cell(cell.Key, CellFlag.DeleteCell));
                }
            }

            Assert.AreEqual(Count, this.GetCellCount());

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cells);
            }

            Assert.AreEqual(0, this.GetCellCount());

            Cell cellDelete;
            key = new Key();
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                key.Row = Guid.NewGuid().ToString();
                key.ColumnFamily = "a";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.ColumnFamily = "b";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                cellDelete = new Cell(key, CellFlag.DeleteCell, true);
            }

            Assert.AreEqual(2, this.GetCellCount());

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cellDelete);
            }

            using (var scanner = table.CreateScanner())
            {
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual("a", cell.Key.ColumnFamily);
                }
            }

            Assert.AreEqual(1, this.GetCellCount());

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                key.ColumnFamily = "b";
                key.ColumnQualifier = "1";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.ColumnQualifier = "2";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.ColumnQualifier = "3";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.ColumnQualifier = string.Empty;
                mutator.Set(key, Encoding.GetBytes(key.Row));
                key.ColumnQualifier = "4";
                mutator.Set(key, Encoding.GetBytes(key.Row));
                cellDelete = new Cell(key, CellFlag.DeleteCell, true);
            }

            Assert.AreEqual(6, this.GetCellCount());

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cellDelete);
            }

            Assert.AreEqual(5, this.GetCellCount());

            cellDelete.Key.ColumnQualifier = "1";
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cellDelete);
            }

            Assert.AreEqual(4, this.GetCellCount());

            cellDelete.Key.ColumnQualifier = string.Empty;
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cellDelete);
            }

            Assert.AreEqual(3, this.GetCellCount());

            cellDelete.Key.ColumnQualifier = null;
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cellDelete);
            }

            using (var scanner = table.CreateScanner())
            {
                while (scanner.Next(out cell))
                {
                    Assert.AreEqual("a", cell.Key.ColumnFamily);
                }
            }

            Assert.AreEqual(1, this.GetCellCount());

            cellDelete.Key.ColumnFamily = string.Empty; // usually use null
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cellDelete);
            }

            Assert.AreEqual(0, this.GetCellCount());

            key = new Key(); // FIXME(remove this line), issue 'delete with unspecified timestamp applying to all future inserts'

            var dateTimes = new List<DateTime>();
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                key.ColumnFamily = "b";
                key.ColumnQualifier = "1";
                key.DateTime = DateTime.UtcNow;
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                Thread.Sleep(200);
                key.DateTime = DateTime.UtcNow;
                mutator.Set(key, Encoding.GetBytes(key.DateTime.ToString()));
                dateTimes.Add(key.DateTime);
                cellDelete = new Cell(key, CellFlag.DeleteCell, true);
            }

            Assert.AreEqual(8, this.GetCellCount());
            Assert.AreEqual(8, dateTimes.Count);

            cells.Clear();
            using (var scanner = table.CreateScanner())
            {
                while (scanner.Next(out cell))
                {
                    cells.Add(cell);
                }
            }

            Assert.AreEqual(8, cells.Count);

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(new Cell(cells[7].Key, CellFlag.DeleteCellVersion, true));
            }

            Assert.AreEqual(7, this.GetCellCount());

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(new Cell(cells[1].Key, CellFlag.DeleteCellVersion, true));
            }

            Assert.AreEqual(6, this.GetCellCount());

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(new Cell(cells[0].Key, CellFlag.DeleteCellVersion, true));
            }

            Assert.AreEqual(5, this.GetCellCount());

            cellDelete.Key.DateTime = dateTimes[1];
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cellDelete);
            }

            Assert.AreEqual(4, this.GetCellCount());

            cellDelete.Key.ColumnQualifier = null;
            cellDelete.Key.DateTime = dateTimes[3];
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cellDelete);
            }

            Assert.AreEqual(2, this.GetCellCount());

            cellDelete.Key.ColumnFamily = null;
            cellDelete.Key.ColumnQualifier = null;
            cellDelete.Key.DateTime = dateTimes[4];
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cellDelete);
            }

            Assert.AreEqual(1, this.GetCellCount());

            cellDelete.Key.ColumnFamily = null;
            cellDelete.Key.ColumnQualifier = null;
            cellDelete.Key.DateTime = dateTimes[5];
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cellDelete);
            }

            Assert.AreEqual(0, this.GetCellCount());
        }
    }
}
