namespace Hyperlite.Test
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Setting cells in batches, including batches whose values differ in size and batches
    ///     of values large enough to matter to the buffering.
    /// </summary>
    /// <remarks>
    ///     Each scenario below is written once, as a private method taking a
    ///     <see cref="MutatorSpec" />, and run by four tests: with no spec, chunked, queued, and
    ///     chunked and queued together. A scenario that passes unbuffered and fails chunked is
    ///     the interesting case, and naming the four separately is what makes the report say
    ///     which buffering mode broke.
    /// </remarks>
    [TestClass]
    public sealed class TestTableMutatorSetCollection : TableMutatorFixture
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            CreateTable(typeof(TestTableMutatorSetCollection));
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
        public void SetCollection()
        {
            this.SetCollection(null);
        }

        [TestMethod]
        public void SetCollectionChunked()
        {
            this.SetCollection(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionChunkedQueued()
        {
            this.SetCollection(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionQueued()
        {
            this.SetCollection(MutatorSpec.CreateQueued());
        }

        [TestMethod]
        public void SetCollectionCreateKey()
        {
            this.SetCollectionCreateKey(null);
        }

        [TestMethod]
        public void SetCollectionCreateKeyChunked()
        {
            this.SetCollectionCreateKey(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionCreateKeyChunkedQueued()
        {
            this.SetCollectionCreateKey(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionCreateKeyQueued()
        {
            this.SetCollectionCreateKey(MutatorSpec.CreateQueued());
        }

        [TestMethod]
        public void SetCollectionCreateKeyLazy()
        {
            this.SetCollectionCreateKeyLazy(null);
        }

        [TestMethod]
        public void SetCollectionCreateKeyLazyChunked()
        {
            this.SetCollectionCreateKeyLazy(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionCreateKeyLazyChunkedQueued()
        {
            this.SetCollectionCreateKeyLazy(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionCreateKeyLazyKeyQueued()
        {
            this.SetCollectionCreateKeyLazy(MutatorSpec.CreateQueued());
        }

        [TestMethod]
        public void SetCollectionDifferentSizedValues()
        {
            this.SetCollectionDifferentSizedValues(null);
        }

        [TestMethod]
        public void SetCollectionDifferentSizedValuesChunked()
        {
            this.SetCollectionDifferentSizedValues(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionDifferentSizedValuesChunkedQueued()
        {
            this.SetCollectionDifferentSizedValues(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionDifferentSizedValuesQueued()
        {
            this.SetCollectionDifferentSizedValues(MutatorSpec.CreateQueued());
        }

        [TestMethod]
        public void SetCollectionHugeValues()
        {
            this.SetCollectionHugeValues(null);
        }

        [TestMethod]
        public void SetCollectionHugeValuesChunked()
        {
            this.SetCollectionHugeValues(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionHugeValuesChunkedQueued()
        {
            this.SetCollectionHugeValues(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionHugeValuesQueued()
        {
            this.SetCollectionHugeValues(MutatorSpec.CreateQueued());
        }

        [TestMethod]
        public void SetCollectionThreaded()
        {
            this.SetCollectionThreaded(null);
        }

        [TestMethod]
        public void SetCollectionThreadedChunked()
        {
            this.SetCollectionThreaded(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionThreadedChunkedQueued()
        {
            this.SetCollectionThreaded(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void SetCollectionThreadedQueued()
        {
            this.SetCollectionThreaded(MutatorSpec.CreateQueued());
        }

        private void SetCollection(MutatorSpec mutatorSpec)
        {
            var key = new Key { ColumnFamily = "a" };
            var cells = new List<Cell>();
            for (var n = 0; n < Count; ++n)
            {
                key.Row = Guid.NewGuid().ToString();
                cells.Add(new Cell(key, Encoding.GetBytes(key.Row), true));
            }

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cells);
            }

            Assert.AreEqual(Count, this.GetCellCount());
        }

        private void SetCollectionCreateKey(MutatorSpec mutatorSpec)
        {
            var key = new Key { ColumnFamily = "a" };
            var cells = new List<Cell>();
            for (var n = 0; n < Count; ++n)
            {
                cells.Add(new Cell(key.Clone() as Key, Encoding.GetBytes(Guid.NewGuid().ToString())));
            }

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cells, true);
            }

            foreach (var cell in cells)
            {
                Assert.IsFalse(string.IsNullOrEmpty(cell.Key.Row));
            }

            Assert.AreEqual(Count, this.GetCellCount());
        }

        private void SetCollectionCreateKeyLazy(MutatorSpec mutatorSpec)
        {
            var key = new Key { ColumnFamily = "a" };
            var cells = new List<Cell>();
            for (var n = 0; n < Count; ++n)
            {
                key.Row = (n % 3) == 0 ? Guid.NewGuid().ToString() : null;
                cells.Add(new Cell(key.Clone() as Key, Encoding.GetBytes(Guid.NewGuid().ToString())));
            }

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cells);
            }

            foreach (var cell in cells)
            {
                Assert.IsFalse(string.IsNullOrEmpty(cell.Key.Row));
            }

            Assert.AreEqual(Count, this.GetCellCount());
        }

        private void SetCollectionDifferentSizedValues(MutatorSpec mutatorSpec)
        {
            var sb = new StringBuilder();
            for (var n = 0; n < 0x40; ++n)
            {
                sb.Append(Guid.NewGuid().ToString());
            }

            var largeValue = Encoding.GetBytes(sb.ToString());
            for (var n = 0; n < 0x4000; ++n)
            {
                sb.Append(Guid.NewGuid().ToString());
            }

            var hugeValue = Encoding.GetBytes(sb.ToString());
            var smallValue = Encoding.GetBytes(Guid.NewGuid().ToString());

            var key = new Key { ColumnFamily = "a" };
            var cells = new List<Cell>
            {
                    new Cell(key.Clone() as Key, smallValue),
                    new Cell(key.Clone() as Key, null),
                    new Cell(key.Clone() as Key, smallValue),
                    new Cell(key.Clone() as Key, largeValue),
                    new Cell(key.Clone() as Key, null),
                    new Cell(key.Clone() as Key, smallValue),
                    new Cell(key.Clone() as Key, largeValue),
                    new Cell(key.Clone() as Key, hugeValue),
                    new Cell(key.Clone() as Key, null),
                    new Cell(key.Clone() as Key, smallValue),
                    new Cell(key.Clone() as Key, largeValue)
                };

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(cells, true);
            }

            foreach (var cell in cells)
            {
                Assert.IsFalse(string.IsNullOrEmpty(cell.Key.Row));
            }
        }

        private void SetCollectionHugeValues(MutatorSpec mutatorSpec)
        {
            const int K = 1024;
            const int M = K * K;
            var hugeValue = new byte[M];
            new Random().NextBytes(hugeValue);

            var key = new Key { ColumnFamily = "a" };
            var cells = new List<Cell>
            {
                    new Cell(key.Clone() as Key, hugeValue),
                    new Cell(key.Clone() as Key, hugeValue),
                    new Cell(key.Clone() as Key, hugeValue),
                    new Cell(key.Clone() as Key, hugeValue),
                    new Cell(key.Clone() as Key, hugeValue),
                    new Cell(key.Clone() as Key, hugeValue),
                    new Cell(key.Clone() as Key, hugeValue),
                    new Cell(key.Clone() as Key, hugeValue),
                    new Cell(key.Clone() as Key, hugeValue),
                    new Cell(key.Clone() as Key, hugeValue),
                    new Cell(key.Clone() as Key, hugeValue)
                };

            using (var mutator = table.CreateMutator())
            {
                mutator.Set(cells, true);
            }

            foreach (var cell in cells)
            {
                Assert.IsFalse(string.IsNullOrEmpty(cell.Key.Row));
            }
        }

        private void SetCollectionThreaded(MutatorSpec mutatorSpec)
        {
            var key = new Key { ColumnFamily = "a" };
            var cells1 = new List<Cell>();
            for (var n = 0; n < 16; ++n)
            {
                cells1.Add(new Cell(key.Clone() as Key, Encoding.GetBytes(Guid.NewGuid().ToString())));
            }

            var cells2 = new List<Cell>();
            for (var n = 0; n < 16; ++n)
            {
                cells2.Add(new Cell(key.Clone() as Key, Encoding.GetBytes(Guid.NewGuid().ToString())));
            }

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                var c1 = 0;
                var c2 = 0;

                var t1 = new Thread(
                    () =>
                        {
                            for (var n = 0; n < Count; ++n, ++c1)
                            {
                                mutator.Set(cells1, true);
                                if (n == Count / 2)
                                {
                                    mutator.Flush();
                                }
                            }
                        });

                var t2 = new Thread(
                    () =>
                        {
                            for (var n = 0; n < Count; ++n, ++c2)
                            {
                                mutator.Set(cells2, true);
                                if (n == Count / 2)
                                {
                                    mutator.Flush();
                                }
                            }
                        });

                t1.Start();
                t2.Start();
                t1.Join();
                t2.Join();
                Assert.IsTrue(c1 > 0);
                Assert.IsTrue(c2 > 0);
            }
        }
    }
}
