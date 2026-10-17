namespace Hyperlite.Persistence.Test.Collections
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Hyperlite.Persistence.Collections;
    using Hyperlite.Persistence.Collections.Concurrent;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Tests <see cref="FlatDictionary{TKey, TValue, TComparer}" /> and
    ///     <see cref="FlatHashSet{T, TComparer}" /> against the framework types they replace.
    /// </summary>
    /// <remarks>
    ///     The flat collections are internal and there is no InternalsVisibleTo here, so the
    ///     project compiles their sources in, the same way the benchmark does - the source
    ///     under test is the shipped one, not a copy.
    ///     <para>
    ///     The randomized cases are the ones that matter. They replay the same operations
    ///     on the flat type and on the framework type and compare after every step, over a
    ///     key range small enough that keys come back after removal, so tombstones, reuse
    ///     of deleted slots, the in-place rebuild and growth all happen. Each runs once with
    ///     a real hash and once with <see cref="CollidingComparer" />, which gives every key
    ///     one of four hash codes: all keys then share a handful of tags and start groups, so
    ///     probe sequences are long and cross many groups, which a good hash never exercises
    ///     at these sizes.
    ///     </para>
    /// </remarks>
    [TestClass]
    public class TestFlatCollections
    {
        [TestMethod]
        public void ConcurrentDictionaryContract()
        {
            var d = new ConcurrentFlatDictionary<string, int, OrdinalComparer>();
            Assert.IsTrue(d.IsEmpty);
            Assert.IsTrue(d.TryAdd("a", 1));
            Assert.IsFalse(d.TryAdd("a", 2));
            Assert.AreEqual(1, d["a"]);
            Assert.ThrowsExactly<KeyNotFoundException>(() => d["missing"]);

            Assert.IsTrue(d.AddOrUpdate("b", 2), "adding reports true");
            Assert.IsFalse(d.AddOrUpdate("b", 3), "replacing reports false");
            Assert.AreEqual(3, d["b"]);
            d["c"] = 4;

            Assert.AreEqual(1, d.GetOrAdd("a", 99));
            Assert.AreEqual(5, d.GetOrAdd("d", 5));
            Assert.AreEqual(6, d.GetOrAdd("e", k => 6));
            Assert.AreEqual(7, d.GetOrAdd("f", 7, (k, x) => x));
            Assert.AreEqual(8, d.GetOrAdd("g", 3, 5, (k, x, y) => x + y));
            Assert.AreEqual(6, d.GetOrAdd("e", k => { throw new InvalidOperationException(); }));

            // snapshots: writes made after one was taken are not in it, and do not break it
            var before = d.Keys;
            d.TryAdd("h", 9);
            Assert.AreEqual(7, before.Count);
            Assert.AreEqual(8, d.Count);
            var seen = 0;
            foreach (var pair in d)
            {
                d.TryAdd("x" + pair.Key, 0);
                ++seen;
            }

            Assert.AreEqual(8, seen);

            int removed;
            Assert.IsTrue(d.TryRemove("a", out removed));
            Assert.AreEqual(1, removed);
            Assert.IsFalse(d.TryRemove("a", out removed));
            Assert.IsTrue(d.Remove("b"));
            Assert.IsFalse(d.ContainsKey("b"));
            CollectionAssert.AreEquivalent(d.Keys.ToList(), d.ToDictionary(p => p.Key, p => p.Value).Keys.ToList());

            var copy = new ConcurrentFlatDictionary<string, int, OrdinalComparer>(d);
            Assert.AreEqual(d.Count, copy.Count);
            d.Clear();
            Assert.AreEqual(0, d.Count);
            Assert.AreNotEqual(0, copy.Count, "a copy does not share the table");
        }

        [TestMethod]
        public void ConcurrentDictionaryReadersNeverSeeATornTable()
        {
            const int Writers = 4;
            const int PerWriter = 2000;
            var d = new ConcurrentFlatDictionary<int, int, IntComparer>();
            var done = 0;
            var failures = 0;
            var readers = Enumerable.Range(0, 3).Select(r => Task.Run(() =>
                {
                    var random = new Random(r);
                    while (Volatile.Read(ref done) == 0)
                    {
                        var key = random.Next(Writers * PerWriter);
                        int value;
                        if (d.TryGetValue(key, out value) && value != key * 2)
                        {
                            Interlocked.Increment(ref failures);
                        }
                    }
                })).ToArray();
            var writers = Enumerable.Range(0, Writers).Select(w => Task.Run(() =>
                {
                    for (var i = w * PerWriter; i < (w + 1) * PerWriter; ++i)
                    {
                        if (i % 3 == 0)
                        {
                            d.GetOrAdd(i, k => k * 2);
                        }
                        else if (i % 3 == 1)
                        {
                            d.TryAdd(i, i * 2);
                        }
                        else
                        {
                            d.AddOrUpdate(i, i * 2);
                        }
                    }
                })).ToArray();
            Task.WaitAll(writers);
            Volatile.Write(ref done, 1);
            Task.WaitAll(readers);

            Assert.AreEqual(0, failures, "a reader saw a value that was never written for its key");
            Assert.AreEqual(Writers * PerWriter, d.Count, "a concurrent write was lost");
            for (var i = 0; i < Writers * PerWriter; ++i)
            {
                Assert.AreEqual(i * 2, d[i]);
            }
        }

        [TestMethod]
        public void InternalStringComparer()
        {
            var comparer = new InternalStringComparer();
            var a = new string('x', 7);
            var b = new string('x', 7);
            Assert.IsFalse(ReferenceEquals(a, b));
            Assert.IsTrue(comparer.Equals(a, b));
            Assert.AreEqual(comparer.GetHashCode(a), comparer.GetHashCode(b), "equal strings, equal hashes");
            Assert.IsFalse(comparer.Equals("abc", "ABC"), "ordinal");

#if NET8_0_OR_GREATER
            // Non-randomized: the same in every process. Computed independently from the
            // runtime's algorithm, covering every length class of its two-characters-at-a-time
            // loop, including the odd ones that read the terminating null.
            Assert.AreEqual(757602046, comparer.GetHashCode(string.Empty));
            Assert.AreEqual(972181027, comparer.GetHashCode("a"));
            Assert.AreEqual(-1882698205, comparer.GetHashCode("ab"));
            Assert.AreEqual(536991770, comparer.GetHashCode("abc"));
            Assert.AreEqual(1594742810, comparer.GetHashCode("abcd"));
            Assert.AreEqual(-1252562527, comparer.GetHashCode("abcde"));
            Assert.AreEqual(449594487, comparer.GetHashCode("bench-reading"));
            Assert.AreEqual(329131308, comparer.GetHashCode("Hyperlite.Persistence"));
#endif

            // good enough to key a table on: distinct names rarely collide
            var names = Enumerable.Range(0, 20000).Select(i => "table-" + i).ToList();
            var distinct = names.Select(comparer.GetHashCode).Distinct().Count();
            Assert.IsTrue(distinct > names.Count * 0.999, "hashes of 20000 names: " + distinct + " distinct");

            var d = new FlatDictionary<string, int, InternalStringComparer>();
            for (var length = 0; length < 40; ++length)
            {
                d.Add(new string('k', length), length);
            }

            for (var length = 0; length < 40; ++length)
            {
                Assert.AreEqual(length, d[new string('k', length)]);
            }
        }

        [TestMethod]
        public void DictionaryAddAndIndexer()
        {
            var d = new FlatDictionary<string, int, OrdinalComparer>();
            Assert.AreEqual(0, d.Count);
            Assert.AreEqual(0, d.Capacity);

            d.Add("a", 1);
            d["b"] = 2;
            d["a"] = 3;
            Assert.AreEqual(2, d.Count);
            Assert.AreEqual(3, d["a"]);
            Assert.AreEqual(2, d["b"]);

            Assert.ThrowsExactly<ArgumentException>(() => d.Add("a", 4));
            Assert.ThrowsExactly<KeyNotFoundException>(() => d["c"]);
            Assert.ThrowsExactly<ArgumentNullException>(() => d.Add(null, 0));
            Assert.ThrowsExactly<ArgumentNullException>(() => d.ContainsKey(null));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new FlatDictionary<string, int, OrdinalComparer>(-1));
        }

        [TestMethod]
        public void DictionaryCapacityHoldsWithoutGrowing()
        {
            foreach (var capacity in new[] { 1, 7, 8, 14, 16, 28, 32, 100, 1000 })
            {
                var d = new FlatDictionary<int, int, IntComparer>(capacity);
                var before = d.Capacity;
                Assert.IsTrue(before >= capacity, "capacity " + capacity);
                for (var i = 0; i < capacity; ++i)
                {
                    d.Add(i, i);
                }

                Assert.AreEqual(before, d.Capacity, "grew below its capacity " + capacity);
            }
        }

        [TestMethod]
        public void DictionaryClearAndReuse()
        {
            var d = new FlatDictionary<int, string, IntComparer>();
            d.Clear();
            for (var i = 0; i < 100; ++i)
            {
                d.Add(i, i.ToString());
            }

            var capacity = d.Capacity;
            d.Clear();
            Assert.AreEqual(0, d.Count);
            Assert.AreEqual(capacity, d.Capacity);
            Assert.IsFalse(d.ContainsKey(5));
            d.Add(5, "five");
            Assert.AreEqual("five", d[5]);

            d.Remove(5);
            d.TrimExcess();
            Assert.AreEqual(0, d.Capacity);
            d.Add(6, "six");
            Assert.AreEqual("six", d[6]);
        }

        [TestMethod]
        public void DictionaryEnumeration()
        {
            var d = new FlatDictionary<int, int, IntComparer>();
            for (var i = 0; i < 50; ++i)
            {
                d.Add(i, i * 10);
            }

            CollectionAssert.AreEquivalent(Enumerable.Range(0, 50).ToList(), d.Keys.ToList());
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 50).Select(i => i * 10).ToList(), d.Values.ToList());
            Assert.AreEqual(50, d.Count(pair => pair.Value == pair.Key * 10));

            // removing while enumerating is allowed, as on .NET Core 3.0 and later
            foreach (var pair in d)
            {
                if (pair.Key % 2 == 0)
                {
                    d.Remove(pair.Key);
                }
            }

            Assert.AreEqual(25, d.Count);
            Assert.IsTrue(d.Keys.All(k => k % 2 == 1));

            // adding is not
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                {
                    foreach (var pair in d)
                    {
                        d.Add(1000 + pair.Key, 0);
                    }
                });

            var array = new KeyValuePair<int, int>[d.Count + 1];
            ((ICollection<KeyValuePair<int, int>>)d).CopyTo(array, 1);
            CollectionAssert.AreEquivalent(d.ToList(), array.Skip(1).ToList());

            var keys = new int[d.Count];
            d.Keys.CopyTo(keys, 0);
            CollectionAssert.AreEquivalent(d.Keys.ToList(), keys);
        }

        [TestMethod]
        public void DictionaryInterfaces()
        {
            IDictionary<string, int> d = new FlatDictionary<string, int, OrdinalComparer>();
            d.Add(new KeyValuePair<string, int>("x", 1));
            Assert.IsTrue(d.Contains(new KeyValuePair<string, int>("x", 1)));
            Assert.IsFalse(d.Contains(new KeyValuePair<string, int>("x", 2)));
            Assert.IsFalse(d.Remove(new KeyValuePair<string, int>("x", 2)));
            Assert.IsTrue(d.Remove(new KeyValuePair<string, int>("x", 1)));
            Assert.AreEqual(0, d.Count);
            Assert.ThrowsExactly<NotSupportedException>(() => d.Keys.Add("y"));

            IReadOnlyDictionary<string, int> r = new FlatDictionary<string, int, OrdinalComparer>(new Dictionary<string, int> { { "p", 7 } });
            Assert.AreEqual(7, r["p"]);
            Assert.AreEqual("p", r.Keys.Single());
        }

        [TestMethod]
        public void DictionarySingleProbeOperations()
        {
            var d = new FlatDictionary<object, int, IdentityComparer>(4);
            var o = new object();

            int value;
            Assert.IsFalse(d.TryGetOrAddValue(o, out value, 1));
            Assert.AreEqual(1, value);
            Assert.IsTrue(d.TryGetOrAddValue(o, out value, 2));
            Assert.AreEqual(1, value);

            Assert.AreEqual(1, d.GetOrAdd(o, 3));
            var p = new object();
            Assert.AreEqual(4, d.GetOrAdd(p, 4));
            Assert.AreEqual(5, d.GetOrAdd(new object(), k => 5));
            Assert.AreEqual(4, d.GetOrAdd(p, k => { throw new InvalidOperationException(); }));

            // a throwing factory leaves nothing behind
            var q = new object();
            Assert.ThrowsExactly<InvalidOperationException>(() => d.GetOrAdd(q, k => { throw new InvalidOperationException(); }));
            Assert.IsFalse(d.ContainsKey(q));

            bool exists;
            d.GetValueRefOrAddDefault(q, out exists) += 10;
            Assert.IsFalse(exists);
            d.GetValueRefOrAddDefault(q, out exists) += 10;
            Assert.IsTrue(exists);
            Assert.AreEqual(20, d[q]);

            Assert.IsTrue(d.TryAdd(new object(), 0));
            Assert.IsFalse(d.TryAdd(q, 0));
            Assert.IsTrue(d.TryRemove(q, out value));
            Assert.AreEqual(20, value);
            Assert.IsFalse(d.TryRemove(q, out value));
        }

        [TestMethod]
        public void DictionaryIdentityIsByReference()
        {
            var d = new FlatDictionary<object, int, IdentityComparer>();
            var a = new EqualToAll();
            var b = new EqualToAll();
            d.Add(a, 1);
            d.Add(b, 2);
            Assert.AreEqual(2, d.Count);
            Assert.AreEqual(1, d[a]);
            Assert.AreEqual(2, d[b]);
        }

        [TestMethod]
        public void DictionaryMatchesFramework()
        {
            DictionaryMatchesFramework<IntComparer>(1, 64);
            DictionaryMatchesFramework<IntComparer>(2, 4096);
            DictionaryMatchesFramework<CollidingComparer>(3, 64);
            DictionaryMatchesFramework<CollidingComparer>(4, 512);
        }

        [TestMethod]
        public void DictionaryOfTypes()
        {
            var d = new FlatDictionary<Type, int, TypeComparer>();
            var types = typeof(string).Assembly.GetTypes().Take(2000).ToList();
            for (var i = 0; i < types.Count; ++i)
            {
                d.Add(types[i], i);
            }

            for (var i = 0; i < types.Count; ++i)
            {
                Assert.AreEqual(i, d[types[i]]);
            }

            Assert.IsTrue(d.ContainsKey(typeof(int)));
            Assert.IsFalse(d.ContainsKey(typeof(TestFlatCollections)));
        }

        [TestMethod]
        public void DictionaryStringKeysIgnoringCase()
        {
            var d = new FlatDictionary<string, int, IgnoreCaseComparer>();
            d.Add("Name", 1);
            Assert.IsTrue(d.ContainsKey("NAME"));
            Assert.IsTrue(d.ContainsKey("name"));
            Assert.IsFalse(d.TryAdd("nAmE", 2));
        }

        [TestMethod]
        public void DictionaryValueTypeKeys()
        {
            var d = new FlatDictionary<Guid, int, GuidComparer>();
            var keys = Enumerable.Range(0, 1000).Select(i => Guid.NewGuid()).ToList();
            foreach (var key in keys)
            {
                d.Add(key, 1);
            }

            Assert.IsTrue(keys.All(d.ContainsKey));
            Assert.IsFalse(d.ContainsKey(Guid.Empty));
            Assert.IsTrue(d.ContainsValue(1));
            Assert.IsFalse(d.ContainsValue(2));
        }

        [TestMethod]
        public void SetBasics()
        {
            var s = new FlatHashSet<object, IdentityComparer>();
            var a = new EqualToAll();
            var b = new EqualToAll();
            Assert.IsTrue(s.Add(a));
            Assert.IsFalse(s.Add(a));
            Assert.IsTrue(s.Add(b));
            Assert.AreEqual(2, s.Count);
            Assert.IsTrue(s.Contains(b));

            object actual;
            Assert.IsTrue(s.TryGetValue(b, out actual));
            Assert.AreSame(b, actual);

            Assert.IsTrue(s.Remove(a));
            Assert.IsFalse(s.Remove(a));
            Assert.IsFalse(s.Contains(a));
            Assert.ThrowsExactly<ArgumentNullException>(() => s.Add(null));

            ISet<object> i = s;
            Assert.IsFalse(i.Add(b));
            i.Clear();
            Assert.AreEqual(0, i.Count);
        }

        [TestMethod]
        public void SetMatchesFramework()
        {
            SetMatchesFramework<IntComparer>(5, 64);
            SetMatchesFramework<IntComparer>(6, 4096);
            SetMatchesFramework<CollidingComparer>(7, 64);
            SetMatchesFramework<CollidingComparer>(8, 512);
        }

        [TestMethod]
        public void SetOperationsMatchFramework()
        {
            var random = new Random(9);
            for (var round = 0; round < 300; ++round)
            {
                var left = Enumerable.Range(0, random.Next(0, 20)).Select(n => random.Next(0, 24)).ToList();
                var right = Enumerable.Range(0, random.Next(0, 20)).Select(n => random.Next(0, 24)).ToList();
                var bcl = new HashSet<int>(left);

                Assert.AreEqual(bcl.IsSubsetOf(right), new FlatHashSet<int, IntComparer>(left).IsSubsetOf(right), "IsSubsetOf");
                Assert.AreEqual(bcl.IsSupersetOf(right), new FlatHashSet<int, IntComparer>(left).IsSupersetOf(right), "IsSupersetOf");
                Assert.AreEqual(bcl.IsProperSubsetOf(right), new FlatHashSet<int, IntComparer>(left).IsProperSubsetOf(right), "IsProperSubsetOf");
                Assert.AreEqual(bcl.IsProperSupersetOf(right), new FlatHashSet<int, IntComparer>(left).IsProperSupersetOf(right), "IsProperSupersetOf");
                Assert.AreEqual(bcl.Overlaps(right), new FlatHashSet<int, IntComparer>(left).Overlaps(right), "Overlaps");
                Assert.AreEqual(bcl.SetEquals(right), new FlatHashSet<int, IntComparer>(left).SetEquals(right), "SetEquals");

                AssertSameSet(Apply(new HashSet<int>(left), s => s.UnionWith(right)), Apply(new FlatHashSet<int, IntComparer>(left), s => s.UnionWith(right)));
                AssertSameSet(Apply(new HashSet<int>(left), s => s.IntersectWith(right)), Apply(new FlatHashSet<int, IntComparer>(left), s => s.IntersectWith(right)));
                AssertSameSet(Apply(new HashSet<int>(left), s => s.ExceptWith(right)), Apply(new FlatHashSet<int, IntComparer>(left), s => s.ExceptWith(right)));
                AssertSameSet(Apply(new HashSet<int>(left), s => s.SymmetricExceptWith(right)), Apply(new FlatHashSet<int, IntComparer>(left), s => s.SymmetricExceptWith(right)));
                AssertSameSet(Apply(new HashSet<int>(left), s => s.RemoveWhere(x => x % 3 == 0)), Apply(new FlatHashSet<int, IntComparer>(left), s => s.RemoveWhere(x => x % 3 == 0)));
            }

            var self = new FlatHashSet<int, IntComparer>(new[] { 1, 2, 3 });
            Assert.IsTrue(self.SetEquals(self));
            Assert.IsFalse(self.IsProperSubsetOf(self));
            self.SymmetricExceptWith(self);
            Assert.AreEqual(0, self.Count);
        }

        private static HashSet<int> Apply(HashSet<int> set, Action<HashSet<int>> action)
        {
            action(set);
            return set;
        }

        private static FlatHashSet<int, IntComparer> Apply(FlatHashSet<int, IntComparer> set, Action<FlatHashSet<int, IntComparer>> action)
        {
            action(set);
            return set;
        }

        private static void AssertSameDictionary<TComparer>(Dictionary<int, int> expected, FlatDictionary<int, int, TComparer> actual, string context)
            where TComparer : struct, IEqualityComparer<int>
        {
            Assert.AreEqual(expected.Count, actual.Count, context);
            var seen = 0;
            foreach (var pair in actual)
            {
                int value;
                Assert.IsTrue(expected.TryGetValue(pair.Key, out value), context + ": unexpected key " + pair.Key);
                Assert.AreEqual(value, pair.Value, context + ": value of " + pair.Key);
                ++seen;
            }

            Assert.AreEqual(expected.Count, seen, context + ": enumerated");
            foreach (var pair in expected)
            {
                Assert.AreEqual(pair.Value, actual[pair.Key], context + ": lookup of " + pair.Key);
            }
        }

        private static void AssertSameSet<TComparer>(HashSet<int> expected, FlatHashSet<int, TComparer> actual)
            where TComparer : struct, IEqualityComparer<int>
        {
            Assert.AreEqual(expected.Count, actual.Count);
            Assert.IsTrue(expected.SetEquals(actual));
            Assert.IsTrue(expected.All(actual.Contains));
        }

        private static void DictionaryMatchesFramework<TComparer>(int seed, int range)
            where TComparer : struct, IEqualityComparer<int>
        {
            var random = new Random(seed);
            var expected = new Dictionary<int, int>();
            var actual = new FlatDictionary<int, int, TComparer>();
            for (var step = 0; step < 20000; ++step)
            {
                var key = random.Next(range);
                var value = random.Next();
                var context = typeof(TComparer).Name + " seed " + seed + " step " + step;
                int e, a;
                switch (random.Next(10))
                {
                    case 0:
                    case 1:
                    case 2:
                        Assert.AreEqual(expected.ContainsKey(key), !actual.TryAdd(key, value), context);
                        if (!expected.ContainsKey(key))
                        {
                            expected.Add(key, value);
                        }

                        break;

                    case 3:
                        expected[key] = value;
                        actual[key] = value;
                        break;

                    case 4:
                    case 5:
                    case 6:
                        Assert.AreEqual(expected.Remove(key), actual.Remove(key), context);
                        break;

                    case 7:
                        Assert.AreEqual(expected.TryGetValue(key, out e), actual.TryGetValue(key, out a), context);
                        Assert.AreEqual(e, a, context);
                        break;

                    case 8:
                        var had = expected.TryGetValue(key, out e);
                        if (!had)
                        {
                            expected.Add(key, e = value);
                        }

                        Assert.AreEqual(had, actual.TryGetOrAddValue(key, out a, value), context);
                        Assert.AreEqual(e, a, context);
                        break;

                    default:
                        if (random.Next(500) == 0)
                        {
                            expected.Clear();
                            actual.Clear();
                        }
                        else if (random.Next(200) == 0)
                        {
                            actual.TrimExcess();
                        }

                        break;
                }

                if (step % 97 == 0)
                {
                    AssertSameDictionary(expected, actual, context);
                }
            }

            AssertSameDictionary(expected, actual, "final");
        }

        private static void SetMatchesFramework<TComparer>(int seed, int range)
            where TComparer : struct, IEqualityComparer<int>
        {
            var random = new Random(seed);
            var expected = new HashSet<int>();
            var actual = new FlatHashSet<int, TComparer>();
            for (var step = 0; step < 20000; ++step)
            {
                var item = random.Next(range);
                var context = typeof(TComparer).Name + " seed " + seed + " step " + step;
                switch (random.Next(8))
                {
                    case 0:
                    case 1:
                    case 2:
                        Assert.AreEqual(expected.Add(item), actual.Add(item), context);
                        break;

                    case 3:
                    case 4:
                    case 5:
                        Assert.AreEqual(expected.Remove(item), actual.Remove(item), context);
                        break;

                    case 6:
                        Assert.AreEqual(expected.Contains(item), actual.Contains(item), context);
                        break;

                    default:
                        if (random.Next(500) == 0)
                        {
                            expected.Clear();
                            actual.Clear();
                        }
                        else if (random.Next(200) == 0)
                        {
                            actual.TrimExcess();
                        }

                        break;
                }

                if (step % 97 == 0)
                {
                    AssertSameSet(expected, actual);
                }
            }

            AssertSameSet(expected, actual);
        }

        /// <summary>
        ///     Four hash codes for every key there is.
        /// </summary>
        private struct CollidingComparer : IEqualityComparer<int>
        {
            public bool Equals(int x, int y)
            {
                return x == y;
            }

            public int GetHashCode(int obj)
            {
                return obj & 3;
            }
        }

        /// <summary>
        ///     Equal to every instance of itself, so only a reference comparer tells two apart.
        /// </summary>
        private sealed class EqualToAll
        {
            public override bool Equals(object obj)
            {
                return obj is EqualToAll;
            }

            public override int GetHashCode()
            {
                return 1;
            }
        }

        private struct GuidComparer : IEqualityComparer<Guid>
        {
            public bool Equals(Guid x, Guid y)
            {
                return x.Equals(y);
            }

            public int GetHashCode(Guid obj)
            {
                return obj.GetHashCode();
            }
        }

        private struct IgnoreCaseComparer : IEqualityComparer<string>
        {
            public bool Equals(string x, string y)
            {
                return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(string obj)
            {
                return System.StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
            }
        }

        private struct IntComparer : IEqualityComparer<int>
        {
            public bool Equals(int x, int y)
            {
                return x == y;
            }

            public int GetHashCode(int obj)
            {
                return obj;
            }
        }

        private struct OrdinalComparer : IEqualityComparer<string>
        {
            public bool Equals(string x, string y)
            {
                return string.Equals(x, y);
            }

            public int GetHashCode(string obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
