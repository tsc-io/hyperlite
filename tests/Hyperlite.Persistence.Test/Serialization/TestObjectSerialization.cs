namespace Hyperlite.Persistence.Test.Serialization
{
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
    ///     Object graphs: plain objects, cycles, structs, and values declared as an interface.
    /// </summary>
    [TestClass]
    public sealed class TestObjectSerialization
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            InvariantCulture.Pin();

            // strict types: these tests store values behind object, an interface or a base class
            Encoder.Configuration.AllowType(typeof(ObjectJ));
            Encoder.Configuration.AllowType(typeof(IfcAImpl));
            Encoder.Configuration.AllowType(typeof(IfcBImpl));
        }

        /// <summary>
        ///     Object graphs round-trip with their property values intact.
        /// </summary>
        [TestMethod]
        public void Objects()
        {
            var oA = new ObjectA();
            var b = Serializer.ToByteArray(oA);
            Assert.IsNotNull(b);
            oA.AssertIsEqualObjectA(Deserializer.FromByteArray<ObjectA>(b));
            oA.AssertIsEqualObjectA_Nullable(Deserializer.FromByteArray<ObjectA_Nullable>(b));

            oA.Randomize();
            b = Serializer.ToByteArray(oA);
            Assert.IsNotNull(b);
            oA.AssertIsEqualObjectA(Deserializer.FromByteArray<ObjectA>(b));
            oA.AssertIsEqualObjectA_Nullable(Deserializer.FromByteArray<ObjectA_Nullable>(b));

            var oAn = new ObjectA_Nullable();
            b = Serializer.ToByteArray(oAn);
            Assert.IsNotNull(b);
            oAn.AssertIsEqualObjectA_Nullable(Deserializer.FromByteArray<ObjectA_Nullable>(b));
            oAn.AssertIsEqualObjectA(Deserializer.FromByteArray<ObjectA>(b));

            oAn.Randomize();
            b = Serializer.ToByteArray(oAn);
            Assert.IsNotNull(b);
            oAn.AssertIsEqualObjectA_Nullable(Deserializer.FromByteArray<ObjectA_Nullable>(b));
            oAn.AssertIsEqualObjectA(Deserializer.FromByteArray<ObjectA>(b));

            var oC = new ObjectC();
            b = Serializer.ToByteArray(oC);
            Assert.IsNotNull(b);
            oC.AssertIsEqualObjectC(Deserializer.FromByteArray<ObjectC>(b));

            oC.Randomize();
            b = Serializer.ToByteArray(oC);
            Assert.IsNotNull(b);
            oC.AssertIsEqualObjectC(Deserializer.FromByteArray<ObjectC>(b));

            oC.Randomize();
            b = Serializer.ToByteArray(oC);
            Assert.IsNotNull(b);
            oC.AssertIsEqualObjectC(Deserializer.FromByteArray<List<ObjectC>>(b)[0]);

            oC.Randomize();
            b = Serializer.ToByteArray(oC);
            Assert.IsNotNull(b);
            oC.AssertIsEqualObjectC(Deserializer.FromByteArray<ObjectC[]>(b)[0]);

            IList<ObjectA> loA = new List<ObjectA>();
            loA.Add(new ObjectA().Randomize());
            loA.Add(new ObjectA().Randomize());
            loA.Add(new ObjectA().Randomize());
            loA.Add(new ObjectA().Randomize());
            loA.Add(new ObjectA().Randomize());

            b = Serializer.ToByteArray(loA);
            Assert.IsNotNull(b);

            var loAr = Deserializer.FromByteArray<IList<ObjectA>>(b);
            Assert.IsTrue(loA.Count == loAr.Count);
            for (var n = 0; n < loA.Count; ++n)
            {
                loA[n].AssertIsEqualObjectA(loAr[n]);
            }

            var loAr2 = Deserializer.FromByteArray<IList<ObjectA_Nullable>>(b);
            Assert.IsTrue(loA.Count == loAr2.Count);
            for (var n = 0; n < loA.Count; ++n)
            {
                loA[n].AssertIsEqualObjectA_Nullable(loAr2[n]);
            }

            var loAr3 = Deserializer.FromByteArray<ISet<ObjectA>>(b);
            Assert.IsTrue(loA.Count == loAr3.Count);

            var loAr4 = Deserializer.FromByteArray<ObjectA[]>(b);
            Assert.IsTrue(loA.Count == loAr4.Length);
            for (var n = 0; n < loA.Count; ++n)
            {
                loA[n].AssertIsEqualObjectA(loAr4[n]);
            }

            b = Serializer.ToByteArray(loA.Distinct());
            Assert.IsNotNull(b);

            var loAr5 = Deserializer.FromByteArray<List<ObjectA>>(b);
            Assert.IsTrue(loA.Count == loAr5.Count);
            for (var n = 0; n < loA.Count; ++n)
            {
                loA[n].AssertIsEqualObjectA(loAr5[n]);
            }

            var aoj = new[] { new ObjectBaseJ(), new ObjectJ() };
            b = Serializer.ToByteArray(aoj);
            Assert.IsNotNull(b);
            var a1 = Deserializer.FromByteArray<ObjectBaseJ[]>(b);
            Assert.AreEqual(2, a1.Length);
            Assert.IsInstanceOfType(a1[0], typeof(ObjectBaseJ));
            Assert.IsInstanceOfType(a1[1], typeof(ObjectJ));

            var a2 = Deserializer.FromByteArray<object[]>(b);
            Assert.AreEqual(2, a2.Length);
            Assert.IsInstanceOfType(a2[0], typeof(ObjectBaseJ));
            Assert.IsInstanceOfType(a2[1], typeof(ObjectJ));

            aoj = new[] { new ObjectBaseJ { A = new ObjectA().Randomize() }, new ObjectJ { A = new ObjectA().Randomize(), C = new ObjectC().Randomize() } };
            b = Serializer.ToByteArray(aoj);
            Assert.IsNotNull(b);
            a1 = Deserializer.FromByteArray<ObjectBaseJ[]>(b);
            Assert.AreEqual(2, a1.Length);
            Assert.IsInstanceOfType(a1[0], typeof(ObjectBaseJ));
            Assert.AreEqual((object)aoj[0].Id, a1[0].Id);
            a1[0].A.AssertIsEqualObjectA(aoj[0].A);
            Assert.IsInstanceOfType(a1[1], typeof(ObjectJ));
            Assert.AreEqual((object)aoj[1].Id, a1[1].Id);
            a1[1].A.AssertIsEqualObjectA(aoj[1].A);
            ((ObjectJ)a1[1]).C.AssertIsEqualObjectC(((ObjectJ)aoj[1]).C);

            a2 = Deserializer.FromByteArray<object[]>(b);
            Assert.AreEqual(2, a2.Length);
            Assert.IsInstanceOfType(a2[0], typeof(ObjectBaseJ));
            ((ObjectBaseJ)a2[0]).A.AssertIsEqualObjectA(aoj[0].A);
            Assert.IsInstanceOfType(a1[1], typeof(ObjectJ));
            ((ObjectJ)a2[1]).A.AssertIsEqualObjectA(aoj[1].A);
            ((ObjectJ)a2[1]).C.AssertIsEqualObjectC(((ObjectJ)aoj[1]).C);
        }

        /// <summary>
        ///     A graph that refers back to itself round-trips without looping.
        /// </summary>
        [TestMethod]
        public void CyclicReferences()
        {
            var oG = new ObjectG();
            var b = Serializer.ToByteArray(oG);
            Assert.IsNotNull(b);
            oG.AssertIsEqualObjectG(Deserializer.FromByteArray<ObjectG>(b));

            oG.Randomize();
            b = Serializer.ToByteArray(oG);
            Assert.IsNotNull(b);
            oG.AssertIsEqualObjectG(Deserializer.FromByteArray<ObjectG>(b));
        }

        /// <summary>
        ///     Structs round-trip, including their nullable form.
        /// </summary>
        [TestMethod]
        public void Structs()
        {
            {
                var sA = new StructA { X = 1.9, Y = 23.4545, Z = 3535.35 };
                var b = Serializer.ToByteArray(sA);
                Assert.IsNotNull(b);
                sA.AssertIsEqualStructA(Deserializer.FromByteArray<StructA>(b));
                sA.AssertIsEqualObjectA_Nullable(Deserializer.FromByteArray<StructA_Nullable>(b));

                sA.AssertIsEqualStructA(Deserializer.FromByteArray<object>(b));
            }

            {
                var av = new[] { new StructA { X = 1.9, Y = 23.4545, Z = 3535.35 }, new StructA { X = 2.9, Y = 53.4545, Z = 7535.35 } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<StructA[]>(b);
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }

            {
                var av = new List<StructA> { new StructA { X = 1.9, Y = 23.4545, Z = 3535.35 }, new StructA { X = 2.9, Y = 53.4545, Z = 7535.35 } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<List<StructA>>(b);
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }

            {
                var sB = new StructB { X = 1.9, Y = 23.4545, Z = 3535.35 };
                var b = Serializer.ToByteArray(sB);
                Assert.IsNotNull(b);
                sB.AssertIsEqualStructB(Deserializer.FromByteArray<StructB>(b));
                sB.AssertIsEqualObjectB_Nullable(Deserializer.FromByteArray<StructB_Nullable>(b));

                sB.AssertIsEqualStructB(Deserializer.FromByteArray<object>(b));
            }

            {
                var bv = new[] { new StructB { X = 1.9, Y = 23.4545, Z = 3535.35 }, new StructB { X = 2.9, Y = 53.4545, Z = 7535.35 } };
                var b = Serializer.ToByteArray(bv);
                Assert.IsNotNull(b);
                var bvr = Deserializer.FromByteArray<StructB[]>(b);
                Assert.IsTrue(Equatable.AreEqual((object)bv, bvr));
            }

            {
                var bv = new List<StructB> { new StructB { X = 1.9, Y = 23.4545, Z = 3535.35 }, new StructB { X = 2.9, Y = 53.4545, Z = 7535.35 } };
                var b = Serializer.ToByteArray(bv);
                Assert.IsNotNull(b);
                var bvr = Deserializer.FromByteArray<List<StructB>>(b);
                Assert.IsTrue(Equatable.AreEqual((object)bv, bvr));

                var bvr2 = Deserializer.FromByteArray<IEnumerable<StructB>>(b);
                Assert.IsInstanceOfType(bvr2, typeof(List<StructB>));
                Assert.IsTrue(Equatable.AreEqual(bv, bvr2));
            }
        }

        /// <summary>
        ///     A value declared as an interface round-trips as its concrete type.
        /// </summary>
        [TestMethod]
        public void Interfaces()
        {
            IfcA iA = new IfcAImpl();
            var b = Serializer.ToByteArray(iA);
            Assert.IsNotNull(b);
            iA.AssertIsEqualIfcA(Deserializer.FromByteArray<IfcA>(b));

            iA.Randomize();
            b = Serializer.ToByteArray(iA);
            Assert.IsNotNull(b);
            iA.AssertIsEqualIfcA(Deserializer.FromByteArray<IfcA>(b));
            iA.AssertIsEqualIfcA(Deserializer.FromByteArray<IfcAImplOther>(b));

            IfcB iB = new IfcBImpl();
            b = Serializer.ToByteArray<IfcA>(iB);
            Assert.IsNotNull(b);
            iB.AssertIsEqualIfcB(Deserializer.FromByteArray<IfcB>(b));

            iB.Randomize();
            b = Serializer.ToByteArray(iB);
            Assert.IsNotNull(b);
            iB.AssertIsEqualIfcB(Deserializer.FromByteArray<IfcB>(b));
            iB.AssertIsEqualIfcA(Deserializer.FromByteArray<IfcA>(b));
            iB.AssertIsEqualIfcB(Deserializer.FromByteArray<IfcBImplOther>(b));

            var oC = new ObjectE();
            b = Serializer.ToByteArray(oC);
            Assert.IsNotNull(b);
            oC.AssertIsEqualObjectE(Deserializer.FromByteArray<ObjectE>(b));

            oC.Randomize();
            b = Serializer.ToByteArray(oC);
            Assert.IsNotNull(b);
            oC.AssertIsEqualObjectE(Deserializer.FromByteArray<ObjectE>(b));
        }
    }
}
