namespace Hyperlite.Test
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Setting a cell: with a key given, with a key created, with a key created lazily, and
    ///     from several threads at once.
    /// </summary>
    /// <remarks>
    ///     Each scenario below is written once, as a private method taking a
    ///     <see cref="MutatorSpec" />, and run by four tests: with no spec, chunked, queued, and
    ///     chunked and queued together. A scenario that passes unbuffered and fails chunked is
    ///     the interesting case, and naming the four separately is what makes the report say
    ///     which buffering mode broke.
    /// </remarks>
    [TestClass]
    public sealed class TestTableMutatorSet : TableMutatorFixture
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            CreateTable(typeof(TestTableMutatorSet));
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
        public void Set()
        {
            this.Set(null);
        }

        [TestMethod]
        public void SetChunked()
        {
            this.Set(new MutatorSpec(MutatorKind.Chunked) { FlushEachChunk = true, MaxCellCount = 100 });
        }

        [TestMethod]
        public void SetChunkedQueued()
        {
            this.Set(new MutatorSpec(MutatorKind.Chunked) { Queued = true, Capacity = 100, FlushEachChunk = true, MaxCellCount = 100 });
        }

        [TestMethod]
        public void SetQueued()
        {
            this.Set(MutatorSpec.CreateQueued());
        }

        [TestMethod]
        public void SetCreateKey()
        {
            this.SetCreateKey(null);
        }

        [TestMethod]
        public void SetCreateKeyChunked()
        {
            this.SetCreateKey(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void SetCreateKeyChunkedQueued()
        {
            this.SetCreateKey(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void SetCreateKeyQueued()
        {
            this.SetCreateKey(MutatorSpec.CreateQueued());
        }

        [TestMethod]
        public void SetCreateKeyLazy()
        {
            this.SetCreateKeyLazy(null);
        }

        [TestMethod]
        public void SetCreateKeyLazyChunked()
        {
            this.SetCreateKeyLazy(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void SetCreateKeyLazyChunkedQueued()
        {
            this.SetCreateKeyLazy(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void SetCreateKeyLazyQueued()
        {
            this.SetCreateKeyLazy(MutatorSpec.CreateQueued());
        }

        [TestMethod]
        public void SetThreaded()
        {
            this.SetThreaded(null);
        }

        [TestMethod]
        public void SetThreadedChunked()
        {
            this.SetThreaded(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void SetThreadedChunkedQueued()
        {
            this.SetThreaded(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void SetThreadedQueued()
        {
            this.SetThreaded(MutatorSpec.CreateQueued());
        }

        /// <summary>
        /// Many mutators in sequence on one table, each writing a single cell.
        /// </summary>
        /// <remarks>
        /// A provider is free to pool the statements or handles a mutator works with instead of
        /// building them per instance, which is what the sqlite provider does. What has to hold is
        /// that what one mutator hands back comes out clean for the next: a stale binding, or one
        /// statement handed to two mutators, shows up here as a missing, a duplicated or a wrong
        /// cell. The delete at the end goes through a mutator of its own, deletes having their own
        /// statements.
        /// </remarks>
        [TestMethod]
        public void SetSingleCellPerMutator()
        {
            const int Mutators = 64;
            const string TableSchema =
                "<Schema><AccessGroup name=\"default\" blksz=\"1024\">" +
                "<ColumnFamily><Name>a</Name></ColumnFamily>" +
                "</AccessGroup></Schema>";

            using (var _table = EnsureTable("SetSingleCellPerMutator", TableSchema))
            {
                var key = new Key { ColumnFamily = "a" };
                for (var n = 0; n < Mutators; ++n)
                {
                    key.Row = n.ToString("D4");
                    using (var mutator = _table.CreateMutator())
                    {
                        mutator.Set(key, Encoding.GetBytes(key.Row));
                    }
                }

                Assert.AreEqual(Mutators, GetCellCount(_table));

                using (var mutator = _table.CreateMutator())
                {
                    mutator.Delete(new Key { Row = 7.ToString("D4") });
                }

                Assert.AreEqual(Mutators - 1, GetCellCount(_table));

                using (var scanner = _table.CreateScanner())
                {
                    Cell cell;
                    while (scanner.Next(out cell))
                    {
                        Assert.AreEqual(cell.Key.Row, Encoding.GetString(cell.Value));
                        Assert.AreNotEqual("0007", cell.Key.Row);
                    }
                }
            }
        }

        /// <summary>
        /// A failing namespace operation does not discard writes a mutator has buffered.
        /// </summary>
        /// <remarks>
        /// The sqlite provider batches a mutator's writes into a transaction and lets a namespace
        /// or table operation open a scope on top of it. A scope which rolls back - and a failing
        /// operation rolls back - must unwind only its own work: the first version of that
        /// transaction handling took the whole pending batch with it, so a row written before an
        /// unrelated failure was simply gone.
        /// </remarks>
        [TestMethod]
        public void SetSurvivesFailingNamespaceOperation()
        {
            const string TableSchema =
                "<Schema><AccessGroup name=\"default\" blksz=\"1024\">" +
                "<ColumnFamily><Name>a</Name></ColumnFamily>" +
                "</AccessGroup></Schema>";

            using (var _table = EnsureTable("SetSurvivesFailingNamespaceOperation", TableSchema))
            {
                var key = new Key { ColumnFamily = "a", Row = "survivor" };
                using (var mutator = _table.CreateMutator())
                {
                    mutator.Set(key, Encoding.GetBytes(key.Row));

                    try
                    {
                        Client.CreateNamespace(NsName);
                        Assert.Fail();
                    }
                    catch (NamespaceExistsException)
                    {
                    }
                    catch
                    {
                        Assert.Fail();
                    }
                }

                Assert.AreEqual(1, GetCellCount(_table));

                using (var scanner = _table.CreateScanner())
                {
                    Cell cell;
                    Assert.IsTrue(scanner.Next(out cell));
                    Assert.AreEqual("survivor", cell.Key.Row);
                    Assert.AreEqual("survivor", Encoding.GetString(cell.Value));
                }
            }
        }

        private void Set(MutatorSpec mutatorSpec)
        {
            var key = new Key { ColumnFamily = "a" };
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                for (var n = 0; n < Count; ++n)
                {
                    key.Row = Guid.NewGuid().ToString();
                    mutator.Set(key, Encoding.GetBytes(key.Row));
                }
            }

            Assert.AreEqual(Count, this.GetCellCount());
        }

        private void SetCreateKey(MutatorSpec mutatorSpec)
        {
            var key = new Key { ColumnFamily = "b" };
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                for (var n = 0; n < Count; ++n)
                {
                    mutator.Set(key, Encoding.GetBytes(Guid.NewGuid().ToString()), true);
                    Assert.IsFalse(string.IsNullOrEmpty(key.Row));
                }
            }

            Assert.AreEqual(Count, this.GetCellCount());
        }

        private void SetCreateKeyLazy(MutatorSpec mutatorSpec)
        {
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                for (var n = 0; n < Count; ++n)
                {
                    var key = new Key { ColumnFamily = "b" };
                    mutator.Set(key, Encoding.GetBytes(Guid.NewGuid().ToString()));
                    Assert.IsFalse(string.IsNullOrEmpty(key.Row));
                }
            }

            Assert.AreEqual(Count, this.GetCellCount());
        }

        private void SetThreaded(MutatorSpec mutatorSpec)
        {
            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                var c1 = 0;
                var c2 = 0;

                var t1 = new Thread(
                    () =>
                        {
                            var key = new Key { ColumnFamily = "a" };
                            for (var n = 0; n < Count; ++n, ++c1)
                            {
                                key.Row = Guid.NewGuid().ToString();
                                mutator.Set(key, Encoding.GetBytes(key.Row));
                            }
                        });

                var t2 = new Thread(
                    () =>
                        {
                            var key = new Key { ColumnFamily = "a" };
                            for (var n = 0; n < Count; ++n, ++c2)
                            {
                                key.Row = Guid.NewGuid().ToString();
                                mutator.Set(key, Encoding.GetBytes(key.Row));
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

        /// <summary>
        ///     One mutator written through by several threads at once loses nothing.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The entity manager keeps one mutator per table and every thread using the
        ///         manager writes through it, so the manager's thread safety rests on this.
        ///         Both kinds the manager can create lock every entry point: the plain one
        ///         around each native call, the chunked one around its buffer as well.
        ///     </para>
        ///     <para>
        ///         Each thread uses a key of its own. A key is the caller's object, and two
        ///         threads sharing one would be racing on it before the mutator saw it.
        ///     </para>
        /// </remarks>
        [TestMethod]
        [Timeout(180000)]
        public void OneMutatorSharedByThreadsLosesNothing()
        {
            const int Threads = 8;

            foreach (var spec in new[] { null, ChunkedMutatorSpec })
            {
                Delete(table);

                using (var mutator = table.CreateMutator(spec))
                {
                    RunConcurrently(Threads, thread =>
                        {
                            var key = new Key { ColumnFamily = "a" };
                            for (var n = 0; n < Count; ++n)
                            {
                                key.Row = thread + "-" + n;
                                mutator.Set(key, Encoding.GetBytes(key.Row));
                            }
                        });
                }

                Assert.AreEqual(
                    Threads * Count,
                    this.GetCellCount(),
                    (spec == null ? "a plain" : "a chunked") + " mutator shared by " + Threads
                    + " threads lost cells");
            }
        }

        /// <summary>
        ///     Runs the body on several threads released together, failing with every
        ///     exception any of them raised.
        /// </summary>
        private static void RunConcurrently(int threads, Action<int> body)
        {
            var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            using (var start = new Barrier(threads))
            {
                var started = new List<Thread>();
                for (var t = 0; t < threads; ++t)
                {
                    var index = t;
                    var thread = new Thread(() =>
                        {
                            try
                            {
                                start.SignalAndWait();
                                body(index);
                            }
                            catch (Exception e)
                            {
                                failures.Enqueue(e);
                            }
                        })
                    { IsBackground = true };
                    thread.Start();
                    started.Add(thread);
                }

                started.ForEach(thread => thread.Join());
            }

            if (!failures.IsEmpty)
            {
                Exception first;
                failures.TryPeek(out first);
                throw new AggregateException(
                    failures.Count + " of " + threads + " threads failed; the first was "
                    + first.GetType().Name + ": " + first.Message,
                    failures);
            }
        }
    }
}
