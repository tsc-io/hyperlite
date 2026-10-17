// The entity fetch path, which nothing else here measures.
//
// The store suite measures the client - cells, keys, scans - and the serialization suite
// measures the serializer with no database under it. Between the two sits the object
// mapper, and its fetch is the part with a cost worth watching: it deserializes on
// Environment.ProcessorCount - 1 tasks and then delivers each entity to the caller's sink
// under a lock, so both the parallelism and the serialization of the hand-off show up here
// and nowhere else.
//
// Cases:
//
//   entity.persist            writing N entities, for context rather than for its own sake
//   entity.fetch.parallel     the default: deserialize on several tasks
//   entity.fetch.sequential   UseParallelDeserialization off, one thread
//   entity.fetch.wide         the same with an entity of twenty properties, where
//                             deserialization is a larger share of the work
//   entity.fetch.counting     the same work with a sink that is thread safe on its own,
//                             which is what prices the delivery lock
//   entity.find               one entity by key, where there is no parallelism to gain
//
// Read the parallel and sequential cases together. The gap between them is what the
// parallelism buys, and a change that serializes too much closes it. Measured, 37
// interleaved passes of nine repetitions each: 2,849 against 4,336 ns per entity, so the
// parallelism is worth about a third of the fetch.
//
// The delivery lock costs 1.0% on the counting case and nothing distinguishable from
// noise on the others, against a 0.05% loss of entities without it - 622 of 1.3 million
// deliveries, all of them in the two cases whose sink is a bare list.Add.

namespace Hyperlite.Bench
{
    using System;
    using System.Collections.Generic;

    using Hyperlite;
    using Hyperlite.Persistence;
    using Hyperlite.Persistence.Attributes;

    internal static class Entities
    {
        /// <summary>
        ///     Entities per fetch case.
        /// </summary>
        /// <remarks>
        ///     Well above the scanner's batch of 64, so the batching and the several
        ///     delivering tasks are all in play. Below the store suite's default because an
        ///     entity is a great deal more work than a cell.
        /// </remarks>
        private const int Count = 2000;

        /// <summary>
        ///     Entities that did not arrive, per case.
        /// </summary>
        /// <remarks>
        ///     Counted rather than thrown on, because a build that loses entities still has
        ///     to produce timings - pricing the delivery lock means running the same cases
        ///     without it, and a throw would leave nothing to compare. Anything other than
        ///     zero here is a defect and not a slow path, so the report names the case and
        ///     the count rather than only saying that something went wrong.
        /// </remarks>
        private static readonly IDictionary<string, int> Lost = new SortedDictionary<string, int>();

        private static void Delivered(string name, int count)
        {
            if (count == Count)
            {
                return;
            }

            int lost;
            Lost[name] = (Lost.TryGetValue(name, out lost) ? lost : 0) + (Count - count);
        }

        internal static void Run(string database)
        {
            var connection = "Provider=SQLite; Ht4n.SQLite.Filename=" + database;

            EntityManagerFactory.DefaultConfiguration.RootNamespace = "bench-entities";

            using (var factory = EntityManagerFactory.CreateEntityManagerFactory(connection))
            {
                // explicitly: the implicit creation on first persist does not cover an
                // entity whose table does not exist yet, it raises TableNotFoundException
                const string Schema =
                    "<Schema><AccessGroup name=\"default\">"
                    + "<ColumnFamily><Name>a</Name></ColumnFamily>"
                    + "</AccessGroup></Schema>";

                foreach (var table in new[] { "bench-reading", "bench-wide" })
                {
                    factory.RootNamespace.DropTable(table, DropDispositions.IfExists);
                    factory.RootNamespace.CreateTable(table, Schema);
                }

                Seed(factory);

                Bench.MeasureCase("entity.fetch.parallel", Count, () => Fetch(factory, true));
                Bench.MeasureCase("entity.fetch.sequential", Count, () => Fetch(factory, false));
                Bench.MeasureCase("entity.fetch.wide", Count, () => FetchWide(factory));
                Bench.MeasureCase("entity.fetch.counting", Count, () => FetchCounting(factory));
                Bench.MeasureCase("entity.find", Count, () => Find(factory));

                foreach (var lost in Lost)
                {
                    Console.WriteLine(
                        "# LOST {0}: {1} entities never reached the sink", lost.Key, lost.Value);
                }

                Lost.Clear();
            }
        }

        /// <summary>
        ///     Writes the entities the fetch cases read, and measures that too.
        /// </summary>
        private static void Seed(EntityManagerFactory factory)
        {
            var keys = new List<Guid>(Count);

            Bench.MeasureCase(
                "entity.persist",
                Count,
                () =>
                    {
                        keys.Clear();
                        using (var em = factory.CreateEntityManager())
                        {
                            for (var i = 0; i < Count; ++i)
                            {
                                var reading = new Reading
                                {
                                    Sensor = "sensor-" + i,
                                    Value = i * 1.5,
                                    Taken = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i)
                                };

                                em.Persist(reading);
                                keys.Add(reading.Id);
                            }
                        }
                    });

            using (var em = factory.CreateEntityManager())
            {
                for (var i = 0; i < Count; ++i)
                {
                    em.Persist(Wide.Populated(i));
                }
            }

            Keys = keys;
        }

        /// <summary>
        ///     The keys written by the seed, for the find case.
        /// </summary>
        private static IList<Guid> Keys { get; set; }

        private static void Fetch(EntityManagerFactory factory, bool parallelDeserialization)
        {
            // a bare list.Add, which is what a caller writes. It is only correct because
            // the fetch serializes delivery; before it did, this lost entities
            var fetched = new List<Reading>(Count);

            using (var em = factory.CreateEntityManager())
            {
                em.Configuration.UseParallelDeserialization = parallelDeserialization;
                em.Fetch<Reading>(fetched.Add);
            }

            Delivered(
                parallelDeserialization ? "entity.fetch.parallel" : "entity.fetch.sequential",
                fetched.Count);
        }

        /// <summary>
        ///     Fetches with a sink that costs almost nothing and is safe either way.
        /// </summary>
        /// <remarks>
        ///     This case exists to price the lock the fetch holds while delivering each
        ///     entity. The other fetch cases cannot: their sink is a bare list.Add, which is
        ///     only correct because of that lock, so a build without it loses entities there
        ///     and measures a fetch that did less work. An interlocked counter is thread safe
        ///     on its own, so this case delivers everything either way and the difference
        ///     between the two builds is the lock - 1.0%, against losing one delivery in two
        ///     thousand.
        /// </remarks>
        private static void FetchCounting(EntityManagerFactory factory)
        {
            var count = 0;

            using (var em = factory.CreateEntityManager())
            {
                em.Configuration.UseParallelDeserialization = true;
                em.Fetch<Reading>(_ => System.Threading.Interlocked.Increment(ref count));
            }

            Delivered("entity.fetch.counting", count);
        }

        private static void FetchWide(EntityManagerFactory factory)
        {
            var fetched = new List<Wide>(Count);

            using (var em = factory.CreateEntityManager())
            {
                em.Fetch<Wide>(fetched.Add);
            }

            Delivered("entity.fetch.wide", fetched.Count);
        }

        private static void Find(EntityManagerFactory factory)
        {
            using (var em = factory.CreateEntityManager())
            {
                for (var i = 0; i < Keys.Count; ++i)
                {
                    var reading = em.Find<Reading>(Keys[i]);
                    if (reading == null)
                    {
                        throw new InvalidOperationException("key " + Keys[i] + " was not found");
                    }
                }
            }
        }

        [Entity("bench-reading", ColumnFamily = "a")]
        internal sealed class Reading
        {
            public Guid Id { get; private set; }

            public string Sensor { get; set; }

            public double Value { get; set; }

            public DateTime Taken { get; set; }
        }

        /// <summary>
        ///     Twenty properties, so that deserialization is a larger share of the fetch
        ///     than the scan and the delivery are.
        /// </summary>
        [Entity("bench-wide", ColumnFamily = "a")]
        internal sealed class Wide
        {
            public Guid Id { get; private set; }

            public bool Bool { get; set; }

            public sbyte SByte { get; set; }

            public byte Byte { get; set; }

            public short Short { get; set; }

            public ushort UShort { get; set; }

            public int Int { get; set; }

            public uint UInt { get; set; }

            public long Long { get; set; }

            public ulong ULong { get; set; }

            public char Char { get; set; }

            public float Float { get; set; }

            public double Double { get; set; }

            public decimal Decimal { get; set; }

            public DateTime DateTime { get; set; }

            public TimeSpan TimeSpan { get; set; }

            public string String { get; set; }

            public Guid Guid { get; set; }

            public Uri Uri { get; set; }

            public int? Nullable { get; set; }

            internal static Wide Populated(int i)
            {
                return new Wide
                {
                    Bool = (i & 1) == 0,
                    SByte = (sbyte)(i % 127),
                    Byte = (byte)(i % 251),
                    Short = (short)i,
                    UShort = (ushort)i,
                    Int = -i,
                    UInt = (uint)i,
                    Long = -(long)i * 1000,
                    ULong = (ulong)i * 1000,
                    Char = (char)('a' + (i % 26)),
                    Float = i * 1.5f,
                    Double = i * 1.5,
                    Decimal = i * 1.5m,
                    DateTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i),
                    TimeSpan = TimeSpan.FromMilliseconds(i),
                    String = "sensor-" + i,
                    Guid = Guid.Empty,
                    Uri = new Uri("http://example.invalid/" + i),
                    Nullable = (i & 1) == 0 ? (int?)i : null
                };
            }
        }
    }
}
