namespace Hyperlite.Persistence.Test
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Hyperlite;
    using Hyperlite.Persistence.Test.TestScannerModesTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestScannerModesTypes
    {
        using System;

        using Hyperlite.Persistence.Attributes;

        [Entity("TestScannerModes", ColumnFamily = "a")]
        internal sealed class Reading
        {
            public Guid Id { get; private set; }

            public string Sensor { get; set; }

            public double Value { get; set; }

            public override bool Equals(object o)
            {
                var other = o as Reading;
                return other != null
                       && string.Equals(this.Sensor, other.Sensor, StringComparison.Ordinal)
                       && this.Value.Equals(other.Value);
            }

            public override int GetHashCode()
            {
                return this.Sensor == null ? 0 : this.Sensor.GetHashCode();
            }
        }
    }

    /// <summary>
    ///     The scanner's deserialization modes and its batch boundary.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>EntityScanner</c> chose between three fetch paths - asynchronous,
    ///         parallel deserialization, and sequential - and a coverage run found only the
    ///         middle one exercised. The asynchronous path could not run at all, needing a
    ///         provider feature that does not exist, and has since been removed along with
    ///         the rest of the asynchronous surface. The sequential path is reachable, by
    ///         configuration, and was not being taken by any test -
    ///         <c>UseParallelDeserialization</c> defaults to true and nothing ever set it
    ///         false.
    ///     </para>
    ///     <para>
    ///         That matters because the two paths have to agree. They read the same bytes
    ///         through different machinery, and a caller who turns parallel deserialization
    ///         off - to make a profile readable, or because a custom decoder is not thread
    ///         safe - must get the same entities back.
    ///     </para>
    ///     <para>
    ///         The batch boundary is the other thing here. The scanner accumulates fetched
    ///         cells into batches of <c>FetchBatchSize</c>, 64, and the code that closes a
    ///         full batch and starts the next only runs above that count. Every other
    ///         fixture in this suite works with a handful of entities, so the boundary was
    ///         never crossed.
    ///     </para>
    /// </remarks>
    [TestClass]
    public class TestScannerModes : TestBase
    {
        /// <summary>
        ///     More than FetchBatchSize, so the batch-closing path runs more than once.
        /// </summary>
        private const int MoreThanOneBatch = 150;

        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            EnsureTable(
                "TestScannerModes",
                "<Schema><AccessGroup name=\"default\">"
                + "<ColumnFamily><Name>a</Name></ColumnFamily>"
                + "</AccessGroup></Schema>");
        }

        /// <summary>
        ///     Sequential and parallel deserialization return the same entities.
        /// </summary>
        [TestMethod]
        public void BothDeserializationModesAgree()
        {
            var expected = Persist(20);

            var parallel = Fetch(true);
            var sequential = Fetch(false);

            Assert.AreEqual(expected.Count, parallel.Count, "the parallel fetch lost entities");
            Assert.AreEqual(
                parallel.Count,
                sequential.Count,
                "the sequential fetch returned a different number of entities than the parallel one");

            CollectionAssert.AreEquivalent(
                parallel,
                sequential,
                "the two deserialization paths returned different entities from the same bytes");
        }

        /// <summary>
        ///     Both modes cross the batch boundary correctly.
        /// </summary>
        /// <remarks>
        ///     150 entities is more than two full batches of 64, so the path that closes a
        ///     batch and starts a new one runs twice and the remainder is flushed at the
        ///     end. An off-by-one there loses or duplicates exactly one batch, which a test
        ///     working with five entities cannot see.
        /// </remarks>
        [TestMethod]
        public void BothModesCrossTheBatchBoundary()
        {
            var expected = Persist(MoreThanOneBatch);

            foreach (var parallelDeserialization in new[] { true, false })
            {
                var fetched = Fetch(parallelDeserialization);

                Assert.AreEqual(
                    expected.Count,
                    fetched.Count,
                    "with parallel deserialization " + parallelDeserialization
                    + ", " + fetched.Count + " entities came back from " + expected.Count
                    + " - a batch was lost or duplicated");

                Assert.AreEqual(
                    fetched.Count,
                    fetched.Distinct().Count(),
                    "duplicate entities came back, so a batch was flushed twice");
            }
        }

        /// <summary>
        ///     Every entity arrives exactly once, whichever mode is used.
        /// </summary>
        /// <remarks>
        ///     Counting is not enough for the parallel path: it deserializes on several
        ///     threads into one sink, so a missing lock shows up as a lost or doubled
        ///     entity rather than as a crash, and only a comparison of the contents finds
        ///     it.
        /// </remarks>
        [TestMethod]
        public void EveryEntityArrivesExactlyOnce()
        {
            var expected = Persist(MoreThanOneBatch);
            var expectedSensors = expected.Select(r => r.Sensor).OrderBy(s => s, StringComparer.Ordinal).ToList();

            foreach (var parallelDeserialization in new[] { true, false })
            {
                var sensors = Fetch(parallelDeserialization)
                    .Select(r => r.Sensor)
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();

                CollectionAssert.AreEqual(
                    expectedSensors,
                    sensors,
                    "with parallel deserialization " + parallelDeserialization
                    + " the set of entities differs from what was written");
            }
        }

        [TestMethod]
        public void ScanSpecAppliesInBothModes()
        {
            Persist(MoreThanOneBatch);

            foreach (var parallelDeserialization in new[] { true, false })
            {
                var fetched = new List<Reading>();

                using (var em = Emf.CreateEntityManager())
                {
                    em.Configuration.UseParallelDeserialization = parallelDeserialization;
                    em.Fetch<Reading>(new ScanSpec { MaxRows = 10 }, fetched.Add);
                }

                Assert.AreEqual(
                    10,
                    fetched.Count,
                    "the scan spec was ignored with parallel deserialization " + parallelDeserialization);
            }
        }

        /// <summary>
        ///     Verbose tracing during a scan does not change the result.
        /// </summary>
        /// <remarks>
        ///     Both fetch paths build a verbose trace message describing the scan, behind an
        ///     <c>IsEnabled</c> check. The message is built by a delegate, so a mistake in
        ///     it - a null table name, a scan spec whose ToString throws - only surfaces
        ///     when tracing is actually on, which no test had turned on.
        /// </remarks>
        [TestMethod]
        public void VerboseTracingDoesNotDisturbAScan()
        {
            var expected = Persist(20);
            var source = Logging.TraceSource;
            var restore = source.Switch.Level;

            try
            {
                source.Switch.Level = System.Diagnostics.SourceLevels.All;

                foreach (var parallelDeserialization in new[] { true, false })
                {
                    var fetched = Fetch(parallelDeserialization);
                    Assert.AreEqual(
                        expected.Count,
                        fetched.Count,
                        "the scan returned a different result with verbose tracing on, "
                        + "parallel deserialization " + parallelDeserialization);
                }
            }
            finally
            {
                source.Switch.Level = restore;
            }
        }

        /// <summary>
        ///     Fetching from a dropped table says so rather than returning nothing.
        /// </summary>
        /// <remarks>
        ///     Both fetch paths check the table and raise a PersistenceException naming it.
        ///     Returning an empty result instead would be the worse outcome: a caller
        ///     cannot tell an empty table from a missing one.
        /// </remarks>
        [TestMethod]
        public void FetchingFromAMissingTableReports()
        {
            Persist(5);

            Emf.RootNamespace.DropTable("TestScannerModes", DropDispositions.IfExists);
            Emf.Clear();

            try
            {
                var fetched = Fetch(true);

                Assert.Fail(
                    "fetching from a dropped table returned " + fetched.Count
                    + " entities instead of reporting the missing table");
            }
            catch (PersistenceException)
            {
            }
            catch (TableNotFoundException)
            {
            }
            finally
            {
                // put it back for whatever runs next
                ClassInitialize(null);
                Emf.Clear();
            }
        }

        /// <summary>
        ///     Fetches everything into a list.
        /// </summary>
        /// <remarks>
        ///     A bare <c>list.Add</c> on purpose. It is the sink a caller would write, and
        ///     before delivery was serialized it lost entities - 147 of 150. Using it here
        ///     means every test in this file exercises that, not just
        ///     <see cref="TheSinkIsNeverCalledConcurrently" />.
        /// </remarks>
        private static List<Reading> Fetch(bool parallelDeserialization)
        {
            var fetched = new List<Reading>();

            using (var em = Emf.CreateEntityManager())
            {
                em.Configuration.UseParallelDeserialization = parallelDeserialization;
                em.Fetch<Reading>(fetched.Add);
            }

            return fetched;
        }

        /// <summary>
        ///     Two calls to the sink never overlap.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A fetch deserializes on <c>Environment.ProcessorCount - 1</c> tasks and
        ///         each of them delivers its results, so the sink used to be invoked
        ///         concurrently. Nothing documented that, and a sink of <c>list.Add</c> -
        ///         which is what a caller writes - lost entities under it: 147 of 150,
        ///         intermittently, on .NET but not on .NET Framework. Delivery is now
        ///         serialized inside the fetch.
        ///     </para>
        ///     <para>
        ///         Counting threads would not show this. Serializing does not pin the sink
        ///         to one thread - it still runs on whichever task deserialized the entity -
        ///         so the property to assert is that no two calls are in flight at once.
        ///         Hence the depth counter rather than a set of thread ids.
        ///     </para>
        ///     <para>
        ///         A caller needing thread *affinity* - touching a UI, say - still cannot
        ///         rely on this: the calls are ordered, not pinned.
        ///     </para>
        /// </remarks>
        [TestMethod]
        public void TheSinkIsNeverCalledConcurrently()
        {
            Persist(MoreThanOneBatch);

            var depth = 0;
            var maxDepth = 0;
            var threads = new HashSet<int>();
            var received = 0;
            var observed = new object();

            using (var em = Emf.CreateEntityManager())
            {
                em.Configuration.UseParallelDeserialization = true;
                em.Fetch<Reading>(_ =>
                    {
                        var now = System.Threading.Interlocked.Increment(ref depth);
                        try
                        {
                            lock (observed)
                            {
                                if (now > maxDepth)
                                {
                                    maxDepth = now;
                                }

                                threads.Add(System.Threading.Thread.CurrentThread.ManagedThreadId);
                                ++received;
                            }

                            // widen the window a reordering would have to survive
                            System.Threading.Thread.Sleep(0);
                        }
                        finally
                        {
                            System.Threading.Interlocked.Decrement(ref depth);
                        }
                    });
            }

            Assert.AreEqual(
                1,
                maxDepth,
                "the sink was entered " + maxDepth + " times at once, so delivery is no "
                + "longer serialized and a sink like list.Add loses entities again");

            Assert.AreEqual(MoreThanOneBatch, received, "entities were lost");

            // an unqualified fetch still takes the parallel branch - the serialization is
            // of the hand-off, not of the deserialization, so the work is still spread out
            if (Environment.ProcessorCount > 2)
            {
                Assert.IsTrue(
                    threads.Count > 1,
                    "the sink ran on one thread only, so either the parallel branch was not "
                    + "taken or deserialization is now serialized too - the second would "
                    + "cost the parallelism the setting exists for");
            }
        }

        /// <summary>
        ///     The natural sink is safe: a bare list.Add receives every entity.
        /// </summary>
        /// <remarks>
        ///     The regression test for the serialization above, written the way a caller
        ///     writes it. Before the fix this lost about 2% of 150 entities, intermittently.
        /// </remarks>
        [TestMethod]
        public void ABareListAddSinkReceivesEverything()
        {
            var expected = Persist(MoreThanOneBatch);

            for (var attempt = 0; attempt < 5; ++attempt)
            {
                var fetched = new List<Reading>();
                using (var em = Emf.CreateEntityManager())
                {
                    em.Configuration.UseParallelDeserialization = true;
                    em.Fetch<Reading>(fetched.Add);
                }

                Assert.AreEqual(
                    expected.Count,
                    fetched.Count,
                    "attempt " + attempt + " received " + fetched.Count + " of "
                    + expected.Count + " through a bare list.Add");
            }
        }

        private static List<Reading> Persist(int count)
        {
            Emf.RootNamespace.DropTable("TestScannerModes", DropDispositions.IfExists);
            ClassInitialize(null);
            Emf.Clear();

            var persisted = new List<Reading>();
            using (var em = Emf.CreateEntityManager())
            {
                for (var i = 0; i < count; ++i)
                {
                    var reading = new Reading { Sensor = "sensor-" + i, Value = i * 1.5 };
                    em.Persist(reading);
                    persisted.Add(reading);
                }
            }

            return persisted;
        }
    }
}
