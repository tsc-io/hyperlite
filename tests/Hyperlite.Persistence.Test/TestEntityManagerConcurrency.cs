namespace Hyperlite.Persistence.Test
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    using Hyperlite.Persistence.Test.TestEntityManagerConcurrencyTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestEntityManagerConcurrencyTypes
    {
        using System;

        using Hyperlite.Persistence.Attributes;

        [Entity("TestEntityManagerConcurrency", ColumnFamily = "a")]
        internal sealed class Item
        {
            public Guid Id { get; private set; }

            public string Name { get; set; }

            public int Number { get; set; }
        }
    }

    /// <summary>
    ///     One entity manager used from several threads at once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An entity manager has to be safe to share between threads - a server handling
    ///         requests on one manager is the ordinary case, not the exotic one. Nothing
    ///         tested that before this fixture, and for one release the manager's own
    ///         bookkeeping was plain <c>HashSet</c>s on the strength of an argument that no
    ///         caller did this. These tests are what should have been asked first.
    ///     </para>
    ///     <para>
    ///         They use threads rather than tasks, released together by a barrier, so that
    ///         the operations really overlap instead of queueing on a thread pool that
    ///         happens to be small. Each runs its concurrent section several times: a race
    ///         in a hash set does not fail every time, and a test that passes once proves
    ///         little.
    ///     </para>
    ///     <para>
    ///         Every test has a timeout. A hash set corrupted by two concurrent writers does
    ///         not always throw - it can link a bucket chain into a cycle, and the next
    ///         lookup never returns. A hang is a failure, and without the timeout it would
    ///         look like a slow machine.
    ///     </para>
    /// </remarks>
    [TestClass]
    public class TestEntityManagerConcurrency : TestBase
    {
        private const int Threads = 8;

        private const int PerThread = 200;

        private const int Rounds = 3;

        private const int TimeoutMs = 180000;

        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            EnsureTable(
                "TestEntityManagerConcurrency",
                "<Schema><AccessGroup name=\"default\">"
                + "<ColumnFamily><Name>a</Name></ColumnFamily>"
                + "</AccessGroup></Schema>");
        }

        /// <summary>
        ///     Several threads persisting through one manager lose nothing.
        /// </summary>
        [TestMethod]
        [Timeout(TimeoutMs)]
        public void ConcurrentPersistsAllArrive()
        {
            for (var round = 0; round < Rounds; ++round)
            {
                ClearNamespace();

                using (var em = Emf.CreateEntityManager())
                {
                    RunConcurrently(t =>
                        {
                            for (var i = 0; i < PerThread; ++i)
                            {
                                em.Persist(new Item { Name = t + "-" + i, Number = i });
                            }
                        });
                }

                var fetched = FetchAll();
                Assert.AreEqual(
                    Threads * PerThread,
                    fetched.Count,
                    "round " + round + ": entities were lost persisting from several threads");
                Assert.AreEqual(
                    Threads * PerThread,
                    fetched.Select(item => item.Name).Distinct().Count(),
                    "round " + round + ": an entity arrived twice");
            }
        }

        /// <summary>
        ///     The same, with <see cref="Behaviors.WriteOnce" />, which routes every persist
        ///     through the manager's shared record of what it has written.
        /// </summary>
        [TestMethod]
        [Timeout(TimeoutMs)]
        public void ConcurrentWriteOncePersistsAllArrive()
        {
            for (var round = 0; round < Rounds; ++round)
            {
                ClearNamespace();

                using (var em = Emf.CreateEntityManager())
                {
                    RunConcurrently(t =>
                        {
                            for (var i = 0; i < PerThread; ++i)
                            {
                                em.Persist(new Item { Name = t + "-" + i, Number = i }, Behaviors.WriteOnce);
                            }
                        });
                }

                Assert.AreEqual(
                    Threads * PerThread,
                    FetchAll().Count,
                    "round " + round + ": entities were lost persisting WriteOnce from several threads");
            }
        }

        /// <summary>
        ///     Several threads fetching through one manager each see everything.
        /// </summary>
        /// <remarks>
        ///     A fetch records every entity it reads in the manager's fetched set, so
        ///     concurrent fetches are concurrent writers to it - and each fetch is itself
        ///     already parallel inside, deserializing on several tasks.
        /// </remarks>
        [TestMethod]
        [Timeout(TimeoutMs)]
        public void ConcurrentFetchesEachSeeEverything()
        {
            const int Seeded = 500;
            Seed(Seeded);

            for (var round = 0; round < Rounds; ++round)
            {
                var counts = new int[Threads];
                using (var em = Emf.CreateEntityManager())
                {
                    RunConcurrently(t =>
                        {
                            var list = new List<Item>();
                            em.Fetch<Item>(list.Add);
                            counts[t] = list.Count;
                        });
                }

                for (var t = 0; t < Threads; ++t)
                {
                    Assert.AreEqual(
                        Seeded,
                        counts[t],
                        "round " + round + ", thread " + t + ": a concurrent fetch did not see every entity");
                }
            }
        }

        /// <summary>
        ///     Persist, fetch and find overlapping on one manager: no exception, nothing lost.
        /// </summary>
        /// <remarks>
        ///     The mixture is the point. A persist consults what the manager has fetched and
        ///     a fetch records it, so the two touch the same state from opposite ends -
        ///     which neither concurrent persists nor concurrent fetches alone exercise.
        /// </remarks>
        [TestMethod]
        [Timeout(TimeoutMs)]
        public void PersistFetchAndFindInterleave()
        {
            const int Seeded = 300;
            var seeded = Seed(Seeded);

            var written = 0;
            using (var em = Emf.CreateEntityManager())
            {
                RunConcurrently(t =>
                    {
                        switch (t % 3)
                        {
                            case 0:
                                for (var i = 0; i < PerThread; ++i)
                                {
                                    em.Persist(new Item { Name = "new-" + t + "-" + i, Number = i });
                                    Interlocked.Increment(ref written);
                                }

                                break;

                            case 1:
                                for (var i = 0; i < 5; ++i)
                                {
                                    var list = new List<Item>();
                                    em.Fetch<Item>(list.Add);
                                    Assert.IsTrue(
                                        list.Count >= Seeded,
                                        "a fetch overlapping persists saw fewer than the seeded entities");
                                }

                                break;

                            default:
                                foreach (var id in seeded)
                                {
                                    Assert.IsNotNull(
                                        em.Find<Item>(id),
                                        "a find overlapping persists and fetches lost a seeded entity");
                                }

                                break;
                        }
                    });
            }

            Assert.AreEqual(Seeded + written, FetchAll().Count, "the interleaved persists lost entities");
        }

        /// <summary>
        ///     Every operation on a disposed manager says so, and says it properly.
        /// </summary>
        /// <remarks>
        ///     The manager used to null its context on dispose, so a call that had already
        ///     passed its disposed check dereferenced null. An
        ///     <c>ObjectDisposedException</c> tells the caller what happened; a
        ///     <c>NullReferenceException</c> tells them the library is broken.
        /// </remarks>
        [TestMethod]
        public void OperationsAfterDisposeThrowObjectDisposed()
        {
            var em = Emf.CreateEntityManager();
            em.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(() => em.Persist(new Item { Name = "x" }));
            Assert.ThrowsExactly<ObjectDisposedException>(() => em.Fetch<Item>().ToList());
            Assert.ThrowsExactly<ObjectDisposedException>(() => em.Find<Item>(Guid.NewGuid()));

            // and a second dispose is not an error
            em.Dispose();
        }

        /// <summary>
        ///     Several threads disposing one manager at once: it is disposed once, cleanly.
        /// </summary>
        [TestMethod]
        [Timeout(TimeoutMs)]
        public void ConcurrentDisposeIsSafe()
        {
            for (var round = 0; round < Rounds * 10; ++round)
            {
                var em = Emf.CreateEntityManager();
                em.Persist(new Item { Name = "dispose-" + round });
                RunConcurrently(t => em.Dispose());
                Assert.ThrowsExactly<ObjectDisposedException>(() => em.Persist(new Item { Name = "late" }));
            }
        }

        /// <summary>
        ///     Runs the body on <see cref="Threads" /> threads that start together, and fails
        ///     with every exception any of them raised.
        /// </summary>
        private static void RunConcurrently(Action<int> body)
        {
            var failures = new ConcurrentQueue<Exception>();
            using (var start = new Barrier(Threads))
            {
                var threads = Enumerable.Range(0, Threads)
                    .Select(t => new Thread(() =>
                        {
                            try
                            {
                                start.SignalAndWait();
                                body(t);
                            }
                            catch (Exception e)
                            {
                                failures.Enqueue(e);
                            }
                        })
                    { IsBackground = true, Name = "concurrency-" + t })
                    .ToList();

                threads.ForEach(thread => thread.Start());
                threads.ForEach(thread => thread.Join());
            }

            if (!failures.IsEmpty)
            {
                throw new AggregateException(
                    failures.Count + " of " + Threads + " threads failed; the first was "
                    + failures.First().GetType().Name + ": " + failures.First().Message,
                    failures);
            }
        }

        private static List<Guid> Seed(int count)
        {
            var items = Enumerable.Range(0, count)
                .Select(i => new Item { Name = "seed-" + i, Number = i })
                .ToList();

            using (var em = Emf.CreateEntityManager())
            {
                foreach (var item in items)
                {
                    em.Persist(item);
                }
            }

            return items.Select(item => item.Id).ToList();
        }

        private static List<Item> FetchAll()
        {
            var list = new List<Item>();
            using (var em = Emf.CreateEntityManager())
            {
                em.Fetch<Item>(list.Add);
            }

            return list;
        }
    }
}
