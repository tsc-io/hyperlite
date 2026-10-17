// The flat collections against the framework's, on the key types this library uses and
// with the comparer each call site passes today.
//
//   identity  object keys, IdentityComparer         the serializer's IdentityRefs,
//                                                   EntityWriter's IdentitySet
//   string    string keys, the ordinal struct       the serializer's StringRefs
//   type      Type keys; the framework's default    the serializer's TypeRefs,
//             comparer against TypeComparer         TypeSchemaRefs and encoderInfos
//   key       Key keys, KeyComparer                 TableScanAndFilter
//
// Three shapes per key type, and the first is the one that decides. The history: a
// microbenchmark of lookups in a warm table said the
// last hand-written dictionary was 15-45% faster, and end to end it was 2.9% slower,
// because the serializer builds its tables more than it looks things up in them.
//
//   build  construct at the capacity the call site asks for, then get-or-add every key
//          twice - a miss that inserts, then a hit - which is what WriteOrAddRef does
//   hit    look up keys that are there, in a table built beforehand
//   miss   look up keys that are not
//
// The framework's get-or-add is TryGetValue then Add, as DictionaryExtensions has it; the
// flat one is TryGetOrAddValue, a single probe. That is a real difference between the
// two and it is measured, not factored out.
//
// Two further cases ask whether the library's static caches should change, and there .bcl is
// the code as it is and .flat the proposal:
//
//   cache.type.hit  a Type-keyed ConcurrentDictionary - what ConcurrentTypeDictionary is, read
//                   3-4 times per object written - against a copy-on-write flat table read
//                   through a volatile field, which is what a read-mostly cache could be
//   valuetype       TypeExtensions.IsValueType, which caches Type.IsValueType in a
//                   ConcurrentDictionary and runs once per object written, against asking
//                   the Type directly
//
// And the concurrent flat collections against the framework's, .flat being the flat one:
//
//   cache.type.hit, cache.string.hit    a read of a read-mostly cache, ConcurrentDictionary
//                                       against ConcurrentFlatDictionary
//   cache.type.fill                     filling that cache from empty, where copy-on-write
//                                       pays a table copy per write
//   cache.string.internal.hit           the string cache again, the flat side keyed on
//                                       InternalStringComparer's non-randomized hash
//   concurrent.fill.cow                 a map written once per entity from three threads -
//                                       EntityReader.entitiesFetched - where copy-on-write
//                                       is expected to lose, measured rather than assumed
//   concurrent.fill.locked              the same load on a flat table behind one lock, which
//                                       says whether a write-concurrent flat map is worth
//                                       designing at all
//
// ns.per.op is per dictionary operation: a build case of n keys performs 2n.
//
// Every framework case runs twice, as .bcl and .bcl.aa. The two run identical code, so the
// gap between them is this harness's own noise, measured on the same case in the same
// process. A flat result inside that gap is not a result. The three arms of a case run in
// an order that rotates every repetition - use a multiple of three - because a fixed order
// turned out to favour whichever arm ran later, see below. Run with --samples to get every
// repetition's timing, which is what lets two arms be compared within one repetition.
//
// Tiered compilation is the other thing that moves a position. On .NET Core a method starts
// as unoptimised tier-0 code and is promoted in the background after a delay that restarts
// whenever something new is jitted - which this suite does constantly - so whatever runs later
// ran on better code. Measured before this was fixed: .bcl.aa beat .bcl in 120 of 260 pairs
// on net8.0 and 129 on net10.0, against 81 on net48, which does not tier. So the loops here
// are optimised from their first call, and the whole suite runs once untimed and then waits
// for the background compiler before anything is measured. The library code the loops call,
// the framework's included, still tiers and profiles as it would in production.
//
// Each side builds at the capacity it ships with: the framework at 32, the flat table at
// 28, which is exactly 32 slots - see ScratchCapacity in Serializer.cs.

namespace Hyperlite.Bench
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;

    using Hyperlite;
    using Hyperlite.Persistence.Collections;
    using Hyperlite.Persistence.Collections.Concurrent;

    using OrdinalComparer = Hyperlite.Persistence.Collections.StringComparer;

    internal static class Collections
    {
        /// <summary>
        ///     Dictionary operations per case, so a case lasts tens of milliseconds whatever n is.
        /// </summary>
        private const int OperationsPerCase = 2000000;

        /// <summary>
        ///     The capacity the serializer's scratch dictionaries ask for on the framework.
        /// </summary>
        private const int ScratchCapacity = 32;

        /// <summary>
        ///     The capacity they ask for as flat tables: 28 is exactly 32 slots.
        /// </summary>
        private const int FlatScratchCapacity = 28;

        private static readonly int[] Sizes = { 4, 16, 64, 1024 };

        /// <summary>
        ///     Whether cases are measured, or only run to warm the code up.
        /// </summary>
        private static bool recording;

        /// <summary>
        ///     The position the first arm of every case starts at in this repetition.
        /// </summary>
        private static int rotation;

        /// <summary>
        ///     Runs the whole suite untimed, twice, waiting for the background compiler after
        ///     each pass, so that no case is measured on tier-0 code.
        /// </summary>
        internal static void WarmUp()
        {
            recording = false;
            for (var pass = 0; pass < 2; ++pass)
            {
                Run();
                Thread.Sleep(1500);
            }

            recording = true;
        }

        internal static void Run()
        {
            if (recording)
            {
                ++rotation;
            }

            foreach (var n in Sizes)
            {
                var objects = Enumerable.Range(0, 2 * n).Select(i => new object()).ToArray();
                Suite("identity", n, objects, new IdentityComparer(), new IdentityComparer());

                var strings = Enumerable.Range(0, 2 * n).Select(i => "property" + i).ToArray();
                Suite("string", n, strings, new OrdinalComparer(), new OrdinalComparer());

                var types = typeof(object).Assembly.GetTypes().Take(2 * n).ToArray();
                Suite<Type, TypeComparer>("type", n, types, null, new TypeComparer());

                var keys = Enumerable.Range(0, 2 * n)
                    .Select(i => new Key("row" + (i / 4), "cf", "cq" + (i % 4)))
                    .ToArray();
                Suite("key", n, keys, new KeyComparer(false), new KeyComparer(false));

                SetSuite("identity", n, objects);
            }

            CacheSuite(16);
            CacheSuite(256);
            ValueTypeSuite(64);
            ConcurrentFillSuite(2000);
        }

        /// <summary>
        ///     A Type-keyed cache as the library has it, against a copy-on-write flat table.
        /// </summary>
        private static void CacheSuite(int n)
        {
            var types = typeof(object).Assembly.GetTypes().Take(n).ToArray();
            var concurrent = new ConcurrentDictionary<Type, int>();
            var flat = new FlatDictionary<Type, int, TypeComparer>(n);
            for (var i = 0; i < types.Length; ++i)
            {
                concurrent.TryAdd(types[i], i);
                flat.Add(types[i], i);
            }

            var cow = new ConcurrentFlatDictionary<Type, int, TypeComparer>(flat);
            var rounds = Math.Max(1, OperationsPerCase / types.Length);
            LookupConcurrent(concurrent, types, 1);
            LookupConcurrentFlat(cow, types, 1);
            Trio("coll.cache.type.hit." + n, rounds * types.Length,
                () => GC.KeepAlive(LookupConcurrent(concurrent, types, rounds)),
                () => GC.KeepAlive(LookupConcurrentFlat(cow, types, rounds)));

            // filling it from empty, which is where copy-on-write pays
            var fills = Math.Max(1, OperationsPerCase / 20 / types.Length);
            FillConcurrent(types, 1);
            FillConcurrentFlat(types, 1);
            Trio("coll.cache.type.fill." + n, fills * types.Length,
                () => GC.KeepAlive(FillConcurrent(types, fills)),
                () => GC.KeepAlive(FillConcurrentFlat(types, fills)));

            var names = Enumerable.Range(0, n).Select(i => "table-" + i).ToArray();
            var byName = new ConcurrentDictionary<string, int>();
            var byNameFlat = new ConcurrentFlatDictionary<string, int, OrdinalComparer>();
            for (var i = 0; i < names.Length; ++i)
            {
                byName.TryAdd(names[i], i);
                byNameFlat.TryAdd(names[i], i);
            }

            var stringRounds = Math.Max(1, OperationsPerCase / names.Length);
            LookupConcurrent(byName, names, 1);
            LookupConcurrentFlat(byNameFlat, names, 1);
            Trio("coll.cache.string.hit." + n, stringRounds * names.Length,
                () => GC.KeepAlive(LookupConcurrent(byName, names, stringRounds)),
                () => GC.KeepAlive(LookupConcurrentFlat(byNameFlat, names, stringRounds)));

            var byNameInternal = new ConcurrentFlatDictionary<string, int, InternalStringComparer>();
            for (var i = 0; i < names.Length; ++i)
            {
                byNameInternal.TryAdd(names[i], i);
            }

            LookupConcurrentFlat(byNameInternal, names, 1);
            Trio("coll.cache.string.internal.hit." + n, stringRounds * names.Length,
                () => GC.KeepAlive(LookupConcurrent(byName, names, stringRounds)),
                () => GC.KeepAlive(LookupConcurrentFlat(byNameInternal, names, stringRounds)));
        }

        /// <summary>
        ///     A map written once per entity from several threads, as
        ///     EntityReader.entitiesFetched is: each thread adds its share of the keys and
        ///     looks each up again.
        /// </summary>
        private static void ConcurrentFillSuite(int n)
        {
            var keys = Enumerable.Range(0, n).Select(i => new object()).ToArray();
            var threads = Math.Max(2, Math.Min(3, Environment.ProcessorCount - 1));
            var rounds = Math.Max(1, OperationsPerCase / 40 / n);
            ConcurrentFillBcl(keys, threads, 1);
            ConcurrentFillCow(keys, threads, 1);
            ConcurrentFillLocked(keys, threads, 1);
            Trio("coll.concurrent.fill.cow." + n, rounds * 2 * n,
                () => GC.KeepAlive(ConcurrentFillBcl(keys, threads, rounds)),
                () => GC.KeepAlive(ConcurrentFillCow(keys, threads, rounds)));
            Trio("coll.concurrent.fill.locked." + n, rounds * 2 * n,
                () => GC.KeepAlive(ConcurrentFillBcl(keys, threads, rounds)),
                () => GC.KeepAlive(ConcurrentFillLocked(keys, threads, rounds)));
        }

        /// <summary>
        ///     TypeExtensions.IsValueType as it is, against asking the Type.
        /// </summary>
        private static void ValueTypeSuite(int n)
        {
            var all = typeof(object).Assembly.GetTypes();
            var types = all.Where(t => t.IsValueType).Take(n / 2).Concat(all.Where(t => !t.IsValueType).Take(n / 2)).ToArray();
            var cache = new ConcurrentDictionary<Type, bool>();
            var rounds = Math.Max(1, OperationsPerCase / types.Length);
            ValueTypeCached(cache, types, 1);
            ValueTypeDirect(types, 1);
            Trio("coll.valuetype." + n, rounds * types.Length,
                () => GC.KeepAlive(ValueTypeCached(cache, types, rounds)),
                () => GC.KeepAlive(ValueTypeDirect(types, rounds)));
        }

        private static void Suite<TKey, TComparer>(
            string name, int n, TKey[] all, IEqualityComparer<TKey> bclComparer, TComparer flatComparer)
            where TComparer : struct, IEqualityComparer<TKey>
        {
            var present = all.Take(n).ToArray();
            var absent = all.Skip(n).ToArray();
            var prefix = "coll." + name + ".";
            var rounds = Math.Max(1, OperationsPerCase / (2 * n));

            // warm both, so neither pays the JIT inside a timed case
            BuildBcl(present, bclComparer, 1);
            BuildFlat(present, flatComparer, 1);

            Trio(prefix + "build." + n, rounds * 2 * n,
                () => GC.KeepAlive(BuildBcl(present, bclComparer, rounds)),
                () => GC.KeepAlive(BuildFlat(present, flatComparer, rounds)));

            var bcl = new Dictionary<TKey, int>(ScratchCapacity, bclComparer);
            var flat = new FlatDictionary<TKey, int, TComparer>(FlatScratchCapacity, flatComparer);
            for (var i = 0; i < present.Length; ++i)
            {
                bcl.Add(present[i], i);
                flat.Add(present[i], i);
            }

            var lookups = 2 * rounds;
            Trio(prefix + "hit." + n, lookups * n,
                () => GC.KeepAlive(LookupBcl(bcl, present, lookups)),
                () => GC.KeepAlive(LookupFlat(flat, present, lookups)));
            Trio(prefix + "miss." + n, lookups * n,
                () => GC.KeepAlive(LookupBcl(bcl, absent, lookups)),
                () => GC.KeepAlive(LookupFlat(flat, absent, lookups)));
        }

        private static void SetSuite(string name, int n, object[] all)
        {
            var present = all.Take(n).ToArray();
            var prefix = "coll.set." + name + ".";
            var rounds = Math.Max(1, OperationsPerCase / (2 * n));

            BuildHashSet(present, 1);
            BuildFlatHashSet(present, 1);

            Trio(prefix + "build." + n, rounds * 2 * n,
                () => GC.KeepAlive(BuildHashSet(present, rounds)),
                () => GC.KeepAlive(BuildFlatHashSet(present, rounds)));
        }

        /// <summary>
        ///     Measures one case's three arms - framework, flat, framework again - in an order
        ///     that rotates by one position every repetition. Over a multiple of three
        ///     repetitions each arm runs in each position equally often, so an effect of
        ///     position lands on all three alike instead of on whichever runs first.
        /// </summary>
        private static void Trio(string name, int operations, Action bcl, Action flat)
        {
            var arms = new[]
            {
                new KeyValuePair<string, Action>(".bcl", bcl),
                new KeyValuePair<string, Action>(".flat", flat),
                new KeyValuePair<string, Action>(".bcl.aa", bcl)
            };
            for (var i = 0; i < arms.Length; ++i)
            {
                var arm = arms[(i + rotation) % arms.Length];
                Measure(name + arm.Key, operations, arm.Value);
            }
        }

        private static void Measure(string name, int operations, Action action)
        {
            if (recording)
            {
                Bench.MeasureCase(name, operations, action);
            }
            else
            {
                action();
            }
        }

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long BuildBcl<TKey>(TKey[] keys, IEqualityComparer<TKey> comparer, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                var d = new Dictionary<TKey, int>(ScratchCapacity, comparer);
                for (var pass = 0; pass < 2; ++pass)
                {
                    for (var i = 0; i < keys.Length; ++i)
                    {
                        int value;
                        if (!d.TryGetValue(keys[i], out value))
                        {
                            d.Add(keys[i], value = i);
                        }

                        sum += value;
                    }
                }
            }

            return sum;
        }

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long BuildFlat<TKey, TComparer>(TKey[] keys, TComparer comparer, int rounds)
            where TComparer : struct, IEqualityComparer<TKey>
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                var d = new FlatDictionary<TKey, int, TComparer>(FlatScratchCapacity, comparer);
                for (var pass = 0; pass < 2; ++pass)
                {
                    for (var i = 0; i < keys.Length; ++i)
                    {
                        int value;
                        d.TryGetOrAddValue(keys[i], out value, i);
                        sum += value;
                    }
                }
            }

            return sum;
        }

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long BuildHashSet(object[] items, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                var s = new HashSet<object>(new IdentityComparer());
                for (var pass = 0; pass < 2; ++pass)
                {
                    for (var i = 0; i < items.Length; ++i)
                    {
                        if (s.Add(items[i]))
                        {
                            ++sum;
                        }
                    }
                }
            }

            return sum;
        }

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long BuildFlatHashSet(object[] items, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                var s = new FlatHashSet<object, IdentityComparer>();
                for (var pass = 0; pass < 2; ++pass)
                {
                    for (var i = 0; i < items.Length; ++i)
                    {
                        if (s.Add(items[i]))
                        {
                            ++sum;
                        }
                    }
                }
            }

            return sum;
        }

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long LookupBcl<TKey>(Dictionary<TKey, int> d, TKey[] keys, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                for (var i = 0; i < keys.Length; ++i)
                {
                    int value;
                    if (d.TryGetValue(keys[i], out value))
                    {
                        sum += value;
                    }
                }
            }

            return sum;
        }

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long LookupFlat<TKey, TComparer>(FlatDictionary<TKey, int, TComparer> d, TKey[] keys, int rounds)
            where TComparer : struct, IEqualityComparer<TKey>
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                for (var i = 0; i < keys.Length; ++i)
                {
                    int value;
                    if (d.TryGetValue(keys[i], out value))
                    {
                        sum += value;
                    }
                }
            }

            return sum;
        }

        /// <summary>
        ///     The factory TypeExtensions.IsValueType passes, cached the way a lambda that
        ///     captures nothing is.
        /// </summary>
        private static readonly Func<Type, bool> IsValueTypeFactory = t => t.IsValueType;

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long ValueTypeCached(ConcurrentDictionary<Type, bool> cache, Type[] types, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                for (var i = 0; i < types.Length; ++i)
                {
                    if (cache.GetOrAdd(types[i], IsValueTypeFactory))
                    {
                        ++sum;
                    }
                }
            }

            return sum;
        }

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long ValueTypeDirect(Type[] types, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                for (var i = 0; i < types.Length; ++i)
                {
                    if (types[i].IsValueType)
                    {
                        ++sum;
                    }
                }
            }

            return sum;
        }

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long LookupConcurrent<TKey>(ConcurrentDictionary<TKey, int> d, TKey[] keys, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                for (var i = 0; i < keys.Length; ++i)
                {
                    int value;
                    if (d.TryGetValue(keys[i], out value))
                    {
                        sum += value;
                    }
                }
            }

            return sum;
        }

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long LookupConcurrentFlat<TKey, TComparer>(ConcurrentFlatDictionary<TKey, int, TComparer> d, TKey[] keys, int rounds)
            where TComparer : struct, IEqualityComparer<TKey>
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                for (var i = 0; i < keys.Length; ++i)
                {
                    int value;
                    if (d.TryGetValue(keys[i], out value))
                    {
                        sum += value;
                    }
                }
            }

            return sum;
        }

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long FillConcurrent(Type[] types, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                var d = new ConcurrentDictionary<Type, int>();
                for (var i = 0; i < types.Length; ++i)
                {
                    sum += d.GetOrAdd(types[i], i);
                }
            }

            return sum;
        }

#if NETCOREAPP
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private static long FillConcurrentFlat(Type[] types, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                var d = new ConcurrentFlatDictionary<Type, int, TypeComparer>();
                for (var i = 0; i < types.Length; ++i)
                {
                    sum += d.GetOrAdd(types[i], i);
                }
            }

            return sum;
        }

        private static long ConcurrentFillBcl(object[] keys, int threads, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                // presized as EntityReader's is
                var d = new ConcurrentDictionary<object, int>(Environment.ProcessorCount, 4096, new IdentityComparer());
                System.Threading.Tasks.Parallel.For(0, threads, t =>
                    {
                        for (var i = t; i < keys.Length; i += threads)
                        {
                            d.TryAdd(keys[i], i);
                        }

                        for (var i = t; i < keys.Length; i += threads)
                        {
                            int value;
                            d.TryGetValue(keys[i], out value);
                        }
                    });
                sum += d.Count;
            }

            return sum;
        }

        private static long ConcurrentFillCow(object[] keys, int threads, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                var d = new ConcurrentFlatDictionary<object, int, IdentityComparer>(4096);
                System.Threading.Tasks.Parallel.For(0, threads, t =>
                    {
                        for (var i = t; i < keys.Length; i += threads)
                        {
                            d.TryAdd(keys[i], i);
                        }

                        for (var i = t; i < keys.Length; i += threads)
                        {
                            int value;
                            d.TryGetValue(keys[i], out value);
                        }
                    });
                sum += d.Count;
            }

            return sum;
        }

        private static long ConcurrentFillLocked(object[] keys, int threads, int rounds)
        {
            long sum = 0;
            for (var round = 0; round < rounds; ++round)
            {
                var d = new FlatDictionary<object, int, IdentityComparer>(4096);
                System.Threading.Tasks.Parallel.For(0, threads, t =>
                    {
                        for (var i = t; i < keys.Length; i += threads)
                        {
                            lock (d)
                            {
                                d.TryAdd(keys[i], i);
                            }
                        }

                        for (var i = t; i < keys.Length; i += threads)
                        {
                            int value;
                            lock (d)
                            {
                                d.TryGetValue(keys[i], out value);
                            }
                        }
                    });
                sum += d.Count;
            }

            return sum;
        }

    }
}
