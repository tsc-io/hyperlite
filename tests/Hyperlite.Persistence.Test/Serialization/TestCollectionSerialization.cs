namespace Hyperlite.Persistence.Test.Serialization
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;

    using Hyperlite.Persistence.Serialization;
    using Hyperlite.Persistence.Test.Common;
    using Hyperlite.Persistence.Test.Serialization.SampleTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using Encoder = Hyperlite.Persistence.Serialization.Encoder;

    /// <summary>
    ///     Arrays, lists, dictionaries and tuples.
    /// </summary>
    /// <remarks>
    ///     The three conversion cases belong here rather than in a fixture of their own: each
    ///     writes one shape and reads another, and what they exercise is the collection
    ///     handling rather than a conversion step that exists on its own.
    /// </remarks>
    [TestClass]
    public sealed class TestCollectionSerialization
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            InvariantCulture.Pin();

            // strict types: these tests store values behind object, an interface or a base class
            Encoder.Configuration.AllowType(typeof(ObjectH));
            Encoder.Configuration.AllowType(typeof(ObjectH2));
            Encoder.Configuration.AllowType(typeof(IfcAImpl));
            Encoder.Configuration.AllowType(typeof(IfcBImpl));
            Encoder.Configuration.AllowType(typeof(StructA));
        }

        /// <summary>
        ///     Arrays of each primitive round-trip.
        /// </summary>
        [TestMethod]
        public void PrimitiveArrays()
        {
            {
                var av = new byte[0];
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<byte[]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }

            {
                var av = new byte[3] { 1, 2, 3 };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<byte[]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }

            {
                var av = new byte[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<byte[,]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual(av, avr));
            }

            {
                var av = new byte[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<short[,]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual(av, avr));
            }

            {
                var av = new[] { new byte[3] { 1, 2, 3 }, new byte[3] { 4, 5, 6 } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<byte[][]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
                var avr2 = Deserializer.FromByteArray<short[][]>(b);
                Assert.IsNotNull(avr2);
                Assert.IsTrue(Equatable.AreEqual(av, avr2));
            }

            {
                var av = new[,] { { 1.0f, 2000.0f, 50000.0f }, { 4.4f, 5.5f, 6.6f } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<float[,]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual(av, avr));
            }

            {
                var av = new[,] { { 1.0, 2000.0, 50000.0 }, { 4.4, 5.5, 6.6 } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<double[,]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual(av, avr));
            }

            {
                var av = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<Guid[]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }

            {
                var av = new byte[3] { 1, 2, 3 };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<object>(b);
                Assert.IsNotNull(avr);
                Assert.IsInstanceOfType(avr, typeof(byte[]));
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }
        }

        /// <summary>
        ///     String arrays round-trip.
        /// </summary>
        [TestMethod]
        public void StringArrays()
        {
            {
                var l = new string[0];
                var l2 =
                    Deserializer.FromByteArray<string[]>(
                        Serializer.ToByteArray(l));

                Assert.IsInstanceOfType(l2, typeof(string[]));
                Assert.IsTrue(Equatable.AreEqual(l, l2));
            }

            {
                var l = new string[] { "Abc" };
                var l2 =
                    Deserializer.FromByteArray<string[]>(
                        Serializer.ToByteArray(l));

                Assert.IsInstanceOfType(l2, typeof(string[]));
                Assert.IsTrue(Equatable.AreEqual(l, l2));
            }

            {
                var l = new string[] { "Abc", "Efg" };
                var l2 =
                    Deserializer.FromByteArray<string[]>(
                        Serializer.ToByteArray(l));

                Assert.IsInstanceOfType(l2, typeof(string[]));
                Assert.IsTrue(Equatable.AreEqual(l, l2));
            }

            {
                var l = new string[] { string.Empty };
                var l2 =
                    Deserializer.FromByteArray<string[]>(
                        Serializer.ToByteArray(l));

                Assert.IsInstanceOfType(l2, typeof(string[]));
                Assert.IsTrue(Equatable.AreEqual(l, l2));
            }

            {
                var l = new string[] { null };
                var l2 =
                    Deserializer.FromByteArray<string[]>(
                        Serializer.ToByteArray(l));

                Assert.IsInstanceOfType(l2, typeof(string[]));
                Assert.IsTrue(Equatable.AreEqual(l, l2));
            }
        }

        /// <summary>
        ///     Lists of string round-trip.
        /// </summary>
        [TestMethod]
        public void StringLists()
        {
            {
                var l = new List<string>();
                var l2 =
                    Deserializer.FromByteArray<List<string>>(
                        Serializer.ToByteArray(l));

                Assert.IsInstanceOfType(l2, typeof(List<string>));
                Assert.IsTrue(Equatable.AreEqual(l, l2));
            }

            {
                var l = new List<string> { "Abc" };
                var l2 =
                    Deserializer.FromByteArray<List<string>>(
                        Serializer.ToByteArray(l));

                Assert.IsInstanceOfType(l2, typeof(List<string>));
                Assert.IsTrue(Equatable.AreEqual(l, l2));
            }

            {
                var l = new List<string> { "Abc", "Efg" };
                var l2 =
                    Deserializer.FromByteArray<List<string>>(
                        Serializer.ToByteArray(l));

                Assert.IsInstanceOfType(l2, typeof(List<string>));
                Assert.IsTrue(Equatable.AreEqual(l, l2));
            }

            {
                var l = new List<string> { string.Empty };
                var l2 =
                    Deserializer.FromByteArray<List<string>>(
                        Serializer.ToByteArray(l));

                Assert.IsInstanceOfType(l2, typeof(List<string>));
                Assert.IsTrue(Equatable.AreEqual(l, l2));
            }

            {
                var l = new List<string> { null };
                var l2 =
                    Deserializer.FromByteArray<List<string>>(
                        Serializer.ToByteArray(l));

                Assert.IsInstanceOfType(l2, typeof(List<string>));
                Assert.IsTrue(Equatable.AreEqual(l, l2));
            }
        }

        /// <summary>
        ///     Generic lists round-trip.
        /// </summary>
        [TestMethod]
        public void Lists()
        {
            {
                var lv = new ArrayList();
                var b = Serializer.ToByteArray(lv);
                Assert.IsNotNull(b);
                var alr = Deserializer.FromByteArray<ArrayList>(b);
                Assert.IsNotNull(alr);
                Assert.IsTrue(Equatable.AreEqual(lv, alr));
                var ilvr = Deserializer.FromByteArray<IList>(b);
                Assert.IsNotNull(ilvr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var lvr = Deserializer.FromByteArray<List<byte>>(b);
                Assert.IsNotNull(lvr);
                Assert.IsTrue(Equatable.AreEqual(lv, lvr));
                var ilvgr = Deserializer.FromByteArray<IList<uint>>(b);
                Assert.IsNotNull(ilvgr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var avr = Deserializer.FromByteArray<byte[]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual(avr, lv));
            }

            {
                var lv = new ArrayList(new byte[] { 1, 2, 3 });
                var b = Serializer.ToByteArray(lv);
                Assert.IsNotNull(b);
                var alr = Deserializer.FromByteArray<ArrayList>(b);
                Assert.IsNotNull(alr);
                Assert.IsTrue(Equatable.AreEqual(lv, alr));
                var ilvr = Deserializer.FromByteArray<IList>(b);
                Assert.IsNotNull(ilvr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var lvr = Deserializer.FromByteArray<List<byte>>(b);
                Assert.IsNotNull(lvr);
                Assert.IsTrue(Equatable.AreEqual(lv, lvr));
                var ilvgr = Deserializer.FromByteArray<IList<uint>>(b);
                Assert.IsNotNull(ilvgr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var avr = Deserializer.FromByteArray<byte[]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual(avr, lv));
            }

            {
                IList lv = new ArrayList(new byte[] { 1, 2, 3 });
                var b = Serializer.ToByteArray(lv);
                Assert.IsNotNull(b);
                var alr = Deserializer.FromByteArray<ArrayList>(b);
                Assert.IsNotNull(alr);
                Assert.IsTrue(Equatable.AreEqual(lv, alr));
                var ilvr = Deserializer.FromByteArray<IList>(b);
                Assert.IsNotNull(ilvr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var lvr = Deserializer.FromByteArray<List<byte>>(b);
                Assert.IsNotNull(lvr);
                Assert.IsTrue(Equatable.AreEqual(lv, lvr));
                var ilvgr = Deserializer.FromByteArray<IList<uint>>(b);
                Assert.IsNotNull(ilvgr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var avr = Deserializer.FromByteArray<byte[]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual(avr, lv));
            }

            {
                var lv = new List<byte>(new byte[] { 1, 2, 3 });
                var b = Serializer.ToByteArray(lv);
                Assert.IsNotNull(b);
                var alr = Deserializer.FromByteArray<ArrayList>(b);
                Assert.IsNotNull(alr);
                Assert.IsTrue(Equatable.AreEqual(lv, alr));
                var ilvr = Deserializer.FromByteArray<IList>(b);
                Assert.IsNotNull(ilvr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var lvr = Deserializer.FromByteArray<List<byte>>(b);
                Assert.IsNotNull(lvr);
                Assert.IsTrue(Equatable.AreEqual((object)lv, lvr));
                var ilvgr = Deserializer.FromByteArray<IList<uint>>(b);
                Assert.IsNotNull(ilvgr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var avr = Deserializer.FromByteArray<byte[]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual((object)avr, lv));
            }

            {
                IList<byte> lv = new List<byte>(new byte[] { 1, 2, 3 });
                var b = Serializer.ToByteArray(lv);
                Assert.IsNotNull(b);
                var alr = Deserializer.FromByteArray<ArrayList>(b);
                Assert.IsNotNull(alr);
                Assert.IsTrue(Equatable.AreEqual(alr, lv));
                var ilvr = Deserializer.FromByteArray<IList>(b);
                Assert.IsNotNull(ilvr);
                Assert.IsTrue(Equatable.AreEqual(ilvr, lv));
                var lvr = Deserializer.FromByteArray<List<byte>>(b);
                Assert.IsNotNull(lvr);
                Assert.IsTrue(Equatable.AreEqual((object)lv, lvr));
                var ilvgr = Deserializer.FromByteArray<IList<byte>>(b);
                Assert.IsNotNull(ilvgr);
                Assert.IsTrue(Equatable.AreEqual((object)lv, ilvgr));
                var avr = Deserializer.FromByteArray<byte[]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual((object)lv, avr));

                var isvgr = Deserializer.FromByteArray<ISet<byte>>(b);
                Assert.IsNotNull(isvgr);
                var hsvgr = Deserializer.FromByteArray<HashSet<byte>>(b);
                Assert.IsNotNull(hsvgr);
                var ssvgr = Deserializer.FromByteArray<SortedSet<byte>>(b);
                Assert.IsNotNull(ssvgr);
            }

            {
                ISet<byte> lv = new SortedSet<byte>(new byte[] { 1, 2, 3 });
                var b = Serializer.ToByteArray(lv);
                Assert.IsNotNull(b);
                var isvgr = Deserializer.FromByteArray<ISet<byte>>(b);
                Assert.IsNotNull(isvgr);
                Assert.IsTrue(isvgr.GetType() == typeof(SortedSet<byte>));
            }

            {
                IList<byte> lv = new List<byte>(new byte[] { 1, 2, 3 });
                var b = Serializer.ToByteArray(lv);
                Assert.IsNotNull(b);
                var olv = Deserializer.FromByteArray<object>(b);
                Assert.IsNotNull(olv);
                Assert.IsInstanceOfType(olv, typeof(List<byte>));
                Assert.IsTrue(Equatable.AreEqual(lv, olv));
            }

            {
                ISet<byte> lv = new HashSet<byte>(new byte[] { 1, 2, 3 });
                var b = Serializer.ToByteArray(lv);
                Assert.IsNotNull(b);
                var olv = Deserializer.FromByteArray<object>(b);
                Assert.IsNotNull(olv);
                Assert.IsInstanceOfType(olv, typeof(HashSet<byte>));
                Assert.IsTrue(Equatable.AreEqual(lv, olv));
            }
        }

        /// <summary>
        ///     Dictionaries round-trip.
        /// </summary>
        [TestMethod]
        public void Dictionaries()
        {
            {
                var dv = new Dictionary<string, string>();
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<Dictionary<string, string>>(b);
                Assert.IsNotNull(dr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, dr));
                var idr = Deserializer.FromByteArray<IDictionary<string, string>>(b);
                Assert.IsNotNull(idr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, idr));
            }

            {
                var dv = new Dictionary<string, string> { { "A", "Aa" }, { "B", "Bb" }, { "C", "Cc" } };
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<Dictionary<string, string>>(b);
                Assert.IsNotNull(dr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, dr));
                var idr = Deserializer.FromByteArray<IDictionary<string, string>>(b);
                Assert.IsNotNull(idr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, idr));
                var cdr = Deserializer.FromByteArray<ConcurrentDictionary<string, string>>(b);
                Assert.IsNotNull(cdr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, cdr));
            }

            {
                var dv = new Dictionary<string, string> { { "A", "11" }, { "B", "22" }, { "C", "33" } };
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<Dictionary<string, int>>(b);
                Assert.IsNotNull(dr);
                Assert.IsTrue(Equatable.AreEqual(dv, dr));
            }

            {
                var dv = new Dictionary<string, byte> { { "A", 1 }, { "B", 2 }, { "C", 3 } };
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<Dictionary<string, byte>>(b);
                Assert.IsNotNull(dr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, dr));
            }

            {
                var dv = new Dictionary<byte, string> { { 1, "11" }, { 2, "22" }, { 3, "33" } };
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<Dictionary<byte, string>>(b);
                Assert.IsNotNull(dr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, dr));
            }

            {
                var dv = new Dictionary<byte, byte> { { 1, 11 }, { 2, 22 }, { 3, 33 } };
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<Dictionary<byte, byte>>(b);
                Assert.IsNotNull(dr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, dr));
            }

            {
                var dv = new Dictionary<string, string[]> { { "A", new[] { "11", "111" } }, { "B", new[] { "22", "222" } }, { "C", new[] { "33", "333" } } };
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<Dictionary<string, string[]>>(b);
                Assert.IsNotNull(dr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, dr));
            }

            {
                var dv = new Dictionary<ObjectN, byte> { { new ObjectN("A"), 11 }, { new ObjectN("B"), 22 }, { new ObjectN("C"), 33 } };
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<Dictionary<ObjectN, byte>>(b);
                Assert.IsNotNull(dr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, dr));
            }

            {
                var dv = new Dictionary<byte, ObjectN> { { 11, new ObjectN("A") }, { 22, new ObjectN("B") }, { 33, new ObjectN("C") } };
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<Dictionary<byte, ObjectN>>(b);
                Assert.IsNotNull(dr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, dr));
            }

            {
                var dv = new Dictionary<ObjectN, string[]>
                    {
                       { new ObjectN("A"), new[] { "11", "111" } }, { new ObjectN("B"), new[] { "22", "222" } }, { new ObjectN("C"), new[] { "33", "333" } }
                    };
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<Dictionary<ObjectN, string[]>>(b);
                Assert.IsNotNull(dr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, dr));
            }

            {
                var dv = new ConcurrentDictionary<uint, ObjectN>();
                dv.TryAdd(11, new ObjectN("A"));
                dv.TryAdd(22, new ObjectN("B"));
                dv.TryAdd(33, new ObjectN("C"));
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<Dictionary<uint, ObjectN>>(b);
                Assert.IsNotNull(dr);
                Assert.IsTrue(Equatable.AreEqual((object)dv, dr));
            }

            {
                var dv = new Dictionary<string, string>();
                var b = Serializer.ToByteArray(dv);
                Assert.IsNotNull(b);
                var dr = Deserializer.FromByteArray<object>(b);
                Assert.IsNotNull(dr);
                Assert.IsInstanceOfType(dr, typeof(Dictionary<string, string>));
                Assert.IsTrue(Equatable.AreEqual(dv, dr));
            }

            {
                var l = new Dictionary<Guid, ObjectH2> { { Guid.NewGuid(), new ObjectH2() } };
                var b = Serializer.ToByteArray<object>(l);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<object>(b);
                Assert.IsNotNull(d);
                Assert.IsInstanceOfType(d, typeof(Dictionary<Guid, ObjectH2>));
                var ld = (Dictionary<Guid, ObjectH2>)d;
                Assert.IsTrue(ld.Count == 1);
                Assert.IsNotNull(ld.First().Value);
                Assert.IsInstanceOfType(ld.First().Value, typeof(ObjectH2));
            }
        }

        /// <summary>
        ///     Values written as an enumerable round-trip.
        /// </summary>
        [TestMethod]
        public void Enumerables()
        {
            {
                var lv = new List<byte>();
                var b = Serializer.ToByteArray<IEnumerable>(lv.Distinct());
                Assert.IsNotNull(b);
                var alr = Deserializer.FromByteArray<ArrayList>(b);
                Assert.IsNotNull(alr);
                Assert.IsTrue(Equatable.AreEqual(lv, alr));
                var ilvr = Deserializer.FromByteArray<IList>(b);
                Assert.IsNotNull(ilvr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var lvr = Deserializer.FromByteArray<List<byte>>(b);
                Assert.IsNotNull(lvr);
                Assert.IsTrue(Equatable.AreEqual((object)lv, lvr));
                var ilvgr = Deserializer.FromByteArray<IList<byte>>(b);
                Assert.IsNotNull(ilvgr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var avr = Deserializer.FromByteArray<byte[]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual((object)avr, lv));

                var isvgr = Deserializer.FromByteArray<ISet<byte>>(b);
                Assert.IsNotNull(isvgr);
                var hsvgr = Deserializer.FromByteArray<HashSet<byte>>(b);
                Assert.IsNotNull(hsvgr);
                var ssvgr = Deserializer.FromByteArray<SortedSet<byte>>(b);
                Assert.IsNotNull(ssvgr);
            }

            {
                var lv = new List<byte>(new byte[] { 1, 2, 3 });
                var b = Serializer.ToByteArray(lv.Distinct());
                Assert.IsNotNull(b);
                var alr = Deserializer.FromByteArray<ArrayList>(b);
                Assert.IsNotNull(alr);
                Assert.IsTrue(Equatable.AreEqual(lv, alr));
                var ilvr = Deserializer.FromByteArray<IList>(b);
                Assert.IsNotNull(ilvr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var lvr = Deserializer.FromByteArray<List<byte>>(b);
                Assert.IsNotNull(lvr);
                Assert.IsTrue(Equatable.AreEqual((object)lv, lvr));
                var ilvgr = Deserializer.FromByteArray<IList<byte>>(b);
                Assert.IsNotNull(ilvgr);
                Assert.IsTrue(Equatable.AreEqual(lv, ilvr));
                var avr = Deserializer.FromByteArray<byte[]>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual((object)avr, lv));

                var isvgr = Deserializer.FromByteArray<ISet<byte>>(b);
                Assert.IsNotNull(isvgr);
                var hsvgr = Deserializer.FromByteArray<HashSet<byte>>(b);
                Assert.IsNotNull(hsvgr);
                var ssvgr = Deserializer.FromByteArray<SortedSet<byte>>(b);
                Assert.IsNotNull(ssvgr);
            }

            {
                var l = new List<ObjectH2> { new ObjectH2() };
                var b = Serializer.ToByteArray<object>(l);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<object>(b);
                Assert.IsNotNull(d);
                Assert.IsInstanceOfType(d, typeof(List<ObjectH2>));
                var ld = (List<ObjectH2>)d;
                Assert.IsTrue(ld.Count == 1);
                Assert.IsNotNull(ld[0]);
                Assert.IsInstanceOfType(ld[0], typeof(ObjectH2));
            }
        }

        /// <summary>
        ///     KeyValuePair values round-trip.
        /// </summary>
        [TestMethod]
        public void KeyValuePairs()
        {
            var kvp = new KeyValuePair<int, bool>();
            var b = Serializer.ToByteArray(kvp);
            Assert.IsNotNull(b);
            kvp = Deserializer.FromByteArray<KeyValuePair<int, bool>>(b);
            Assert.IsTrue(kvp.Key == 0);
            Assert.IsFalse(kvp.Value);

            kvp = new KeyValuePair<int, bool>(31, true);
            b = Serializer.ToByteArray(kvp);
            Assert.IsNotNull(b);
            kvp = Deserializer.FromByteArray<KeyValuePair<int, bool>>(b);
            Assert.IsTrue(kvp.Key == 31);
            Assert.IsTrue(kvp.Value);

            var oA = new ObjectA();
            var kvp2 = new KeyValuePair<int, ObjectA>(31, oA);
            b = Serializer.ToByteArray(kvp2);
            Assert.IsNotNull(b);
            kvp2 = Deserializer.FromByteArray<KeyValuePair<int, ObjectA>>(b);
            Assert.IsTrue(kvp2.Key == 31);
            oA.AssertIsEqualObjectA(kvp2.Value);

            IfcA iA = new IfcAImpl();
            IfcB iB = new IfcBImpl();
            var kvp3 = new KeyValuePair<IfcA, IfcB>(iA, iB);
            b = Serializer.ToByteArray(kvp3);
            Assert.IsNotNull(b);
            kvp3 = Deserializer.FromByteArray<KeyValuePair<IfcA, IfcB>>(b);
            iA.AssertIsEqualIfcA(kvp3.Key);
            iB.AssertIsEqualIfcB(kvp3.Value);

            var kvp4 = new KeyValuePair<object, IfcB>(iA, iB);
            b = Serializer.ToByteArray(kvp3);
            Assert.IsNotNull(b);
            kvp4 = Deserializer.FromByteArray<KeyValuePair<object, IfcB>>(b);
            iA.AssertIsEqualIfcA(kvp4.Key);
            iB.AssertIsEqualIfcB(kvp4.Value);
        }

        /// <summary>
        ///     Tuples round-trip, including one holding an object.
        /// </summary>
        [TestMethod]
        public void Tuples()
        {
            var t1 = new Tuple<string>("Test");
            var b = Serializer.ToByteArray(t1);
            Assert.IsNotNull(b);
            t1 = Deserializer.FromByteArray<Tuple<string>>(b);
            Assert.AreEqual("Test", t1.Item1);

            var t2 = new Tuple<int, bool>(3, false);
            b = Serializer.ToByteArray(t2);
            Assert.IsNotNull(b);
            t2 = Deserializer.FromByteArray<Tuple<int, bool>>(b);
            Assert.IsTrue(t2.Item1 == 3);
            Assert.IsFalse(t2.Item2);

            t2 = new Tuple<int, bool>(31, true);
            b = Serializer.ToByteArray(t2);
            Assert.IsNotNull(b);
            t2 = Deserializer.FromByteArray<Tuple<int, bool>>(b);
            Assert.IsTrue(t2.Item1 == 31);
            Assert.IsTrue(t2.Item2);

            var kvp = Deserializer.FromByteArray<KeyValuePair<int, bool>>(b);
            Assert.IsTrue(kvp.Key == 31);
            Assert.IsTrue(kvp.Value);

            t2 = new Tuple<int, bool>(31, true);
            b = Serializer.ToByteArray(t2);
            Assert.IsNotNull(b);
            t2 = Deserializer.FromByteArray<Tuple<int, bool>>(b);
            Assert.IsTrue(t2.Item1 == 31);
            Assert.IsTrue(t2.Item2);

            var oA = new ObjectA();
            var t3 = new Tuple<int, bool, ObjectA>(31, true, oA);
            b = Serializer.ToByteArray(t3);
            Assert.IsNotNull(b);
            t3 = Deserializer.FromByteArray<Tuple<int, bool, ObjectA>>(b);
            Assert.IsTrue(t3.Item1 == 31);
            Assert.IsTrue(t3.Item2);
            oA.AssertIsEqualObjectA(t3.Item3);
        }

        /// <summary>
        ///     An object[] of arrays and objects round-trips with its element types recovered.
        /// </summary>
#pragma warning restore IDE0060 // Remove unused parameter
        [TestMethod]
        public void AnonymousObjectArrays()
        {
            {
                var i = new ObjectI { Objects = new object[] { new[] { new ObjectH { String = "AAA" } } } };
                var b = Serializer.ToByteArray(i);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<ObjectI>(b);
                Assert.IsNotNull(d);
                Assert.IsInstanceOfType(d, typeof(ObjectI));
                Assert.IsNotNull(d.Objects);
                Assert.IsTrue(d.Objects.Length == 1);
                Assert.IsNotNull(d.Objects[0]);
                Assert.IsInstanceOfType(d.Objects[0], typeof(object[]));
                Assert.IsTrue((d.Objects[0] as object[]).Length == 1);
                Assert.IsInstanceOfType((d.Objects[0] as object[])[0], typeof(ObjectH));
                Assert.AreEqual("AAA", ((d.Objects[0] as object[])[0] as ObjectH).String);
            }

            {
                var i = new ObjectI { Objects = new object[] { new[] { new ObjectH2() } } };
                var b = Serializer.ToByteArray(i);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<ObjectI>(b);
                Assert.IsNotNull(d);
                Assert.IsInstanceOfType(d, typeof(ObjectI));
                Assert.IsNotNull(d.Objects);
                Assert.IsTrue(d.Objects.Length == 1);
                Assert.IsNotNull(d.Objects[0]);
                Assert.IsInstanceOfType(d.Objects[0], typeof(object[]));
                Assert.IsTrue((d.Objects[0] as object[]).Length == 1);
                Assert.IsInstanceOfType((d.Objects[0] as object[])[0], typeof(ObjectH2));
            }

            {
                var a = new[] { "a", "b", "c", "d" };
                var b = Serializer.ToByteArray(a);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<object>(b);
                Assert.IsNotNull(d);
                Assert.IsInstanceOfType(d, typeof(string[]));
                var da = (string[])d;
                Assert.IsTrue(da.Length == 4);
                Assert.AreEqual(da[0], "a");
                Assert.AreEqual(da[1], "b");
                Assert.AreEqual(da[2], "c");
                Assert.AreEqual(da[3], "d");
            }

            {
                var a = new ObjectM { Item = new[] { "a", "b", "c", "d" } };
                var b = Serializer.ToByteArray(a);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<ObjectM>(b);
                Assert.IsNotNull(d);
                Assert.IsInstanceOfType(d, typeof(ObjectM));
                Assert.IsInstanceOfType(d.Item, typeof(string[]));
            }

            {
                var a = new[] { "a", "b", "c", "d" };
                var b = Serializer.ToByteArray(a);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<object[]>(b);
                Assert.IsNotNull(d);
                Assert.IsInstanceOfType(d, typeof(object[]));
                Assert.IsTrue(d.Length == 4);
                Assert.AreEqual(d[0], "a");
                Assert.AreEqual(d[1], "b");
                Assert.AreEqual(d[2], "c");
                Assert.AreEqual(d[3], "d");
            }

            {
                var a = new[] { "a", "b", "c", "d" };
                var b = Serializer.ToByteArray(a);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<string[]>(b);
                Assert.IsNotNull(d);
                Assert.IsInstanceOfType(d, typeof(string[]));
                Assert.IsTrue(d.Length == 4);
                Assert.AreEqual(d[0], "a");
                Assert.AreEqual(d[1], "b");
                Assert.AreEqual(d[2], "c");
                Assert.AreEqual(d[3], "d");
            }

            {
                var a = new object[] { "a", "b", 7, 77.77 };
                var b = Serializer.ToByteArray(a);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<object[]>(b);
                Assert.IsNotNull(d);
                Assert.IsInstanceOfType(d, typeof(object[]));
                Assert.IsTrue(d.Length == 4);
                Assert.AreEqual(d[0], "a");
                Assert.AreEqual(d[1], "b");
                Assert.AreEqual(d[2], 7);
                Assert.AreEqual(d[3], 77.77);
            }

            {
                var a = new object[] { "a", "b", 7, 77.77 };
                var b = Serializer.ToByteArray(a);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<string[]>(b);
                Assert.IsNotNull(d);
                Assert.IsInstanceOfType(d, typeof(string[]));
                Assert.IsTrue(d.Length == 4);
                Assert.AreEqual(d[0], "a");
                Assert.AreEqual(d[1], "b");
                Assert.AreEqual(d[2], "7");
                Assert.AreEqual(d[3], "77.77");
            }

            {
                var a = new StructA[] { new StructA(), new StructA() };
                var b = Serializer.ToByteArray(a);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<object>(b);
                Assert.IsNotNull(d);
                Assert.IsInstanceOfType(d, typeof(object[]));
                var da = (object[])d;
                Assert.IsTrue(da.Length == 2);
            }
        }

        /// <summary>
        ///     An array read back as an array of a different element type.
        /// </summary>
        [TestMethod]
        public void ArrayReadAsAnotherElementType()
        {
            {
                var b = Serializer.ToByteArray(1024);
                Assert.IsNotNull(b);
                var av = Deserializer.FromByteArray<int[]>(b);
                Assert.IsTrue(av.Length == 1);
                Assert.IsTrue(av[0] == 1024);

                b = Serializer.ToByteArray<long>(122345);
                Assert.IsNotNull(b);
                av = Deserializer.FromByteArray<int[]>(b);
                Assert.IsTrue(av.Length == 1);
                Assert.IsTrue(av[0] == 122345);
            }

            {
                var b = Serializer.ToByteArray(true);
                Assert.IsNotNull(b);
                var av = Deserializer.FromByteArray<bool[]>(b);
                Assert.IsTrue(av.Length == 1);
                Assert.IsTrue(av[0]);

                b = Serializer.ToByteArray(false);
                Assert.IsNotNull(b);
                av = Deserializer.FromByteArray<bool[]>(b);
                Assert.IsTrue(av.Length == 1);
                Assert.IsFalse(av[0]);
            }
        }

        /// <summary>
        ///     An array read back as a list.
        /// </summary>
        [TestMethod]
        public void ArrayReadAsAList()
        {
            {
                var av = new byte[0];
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<List<byte>>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }

            {
                var av = new byte[3] { 1, 2, 3 };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<List<byte>>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }

            {
                var av = new byte[3] { 1, 2, 3 };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<ArrayList>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual(av, avr));
            }

            {
                var av = new byte[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<List<byte>>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual(av, avr));
            }

            {
                var av = new[] { new byte[3] { 1, 2, 3 }, new byte[3] { 4, 5, 6 } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<List<List<byte>>>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual(av, avr));
            }

            {
                var av = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<List<Guid>>(b);
                Assert.IsNotNull(avr);
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }
        }

        /// <summary>
        ///     A list read back with a different element type.
        /// </summary>
        [TestMethod]
        public void ListReadAsAnotherElementType()
        {
            {
                var b = Serializer.ToByteArray(1024);
                Assert.IsNotNull(b);
                var av = Deserializer.FromByteArray<List<int>>(b);
                Assert.IsTrue(av.Count == 1);
                Assert.IsTrue(av[0] == 1024);

                b = Serializer.ToByteArray<long>(122345);
                Assert.IsNotNull(b);
                av = Deserializer.FromByteArray<List<int>>(b);
                Assert.IsTrue(av.Count == 1);
                Assert.IsTrue(av[0] == 122345);
            }

            {
                var b = Serializer.ToByteArray(true);
                Assert.IsNotNull(b);
                var av = Deserializer.FromByteArray<List<bool>>(b);
                Assert.IsTrue(av.Count == 1);
                Assert.IsTrue(av[0]);

                b = Serializer.ToByteArray(false);
                Assert.IsNotNull(b);
                av = Deserializer.FromByteArray<List<bool>>(b);
                Assert.IsTrue(av.Count == 1);
                Assert.IsFalse(av[0]);
            }

            {
                var b = Serializer.ToByteArray(1024);
                Assert.IsNotNull(b);
                var av = Deserializer.FromByteArray<ArrayList>(b);
                Assert.IsTrue(av.Count == 1);
                Assert.IsTrue((int)av[0] == 1024);

                b = Serializer.ToByteArray<long>(122345);
                Assert.IsNotNull(b);
                av = Deserializer.FromByteArray<ArrayList>(b);
                Assert.IsTrue(av.Count == 1);
                Assert.IsTrue((long)av[0] == 122345);
            }
        }
    }
}
