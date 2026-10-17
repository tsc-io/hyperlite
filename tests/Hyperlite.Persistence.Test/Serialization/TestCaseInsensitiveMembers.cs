namespace Hyperlite.Persistence.Test.Serialization
{
    using System;

    using Hyperlite.Persistence.Serialization;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Members whose names differ only in case, which the inspector matches case-insensitively.
    /// </summary>
    /// <remarks>
    ///     The inspector keys a type's members on <c>StringComparerOrdinalIgnoreCase</c>, whose
    ///     hash used to be case sensitive. Two names differing only in case then compared equal
    ///     but hashed apart, so whether the second one was dropped as a duplicate depended on
    ///     whether the two hashes happened to meet - and on .NET Core string hashes are
    ///     randomized per process, so it could differ from one run to the next. With the hash
    ///     fixed, only one of the two could ever be serialized, so such a type is refused with an
    ///     error naming both, rather than having one member silently dropped.
    /// </remarks>
    [TestClass]
    public sealed class TestCaseInsensitiveMembers
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            InvariantCulture.Pin();
        }

        /// <summary>
        ///     Two properties differing only in case are refused, naming both.
        /// </summary>
        [TestMethod]
        public void CaseOnlyDifferentPropertiesAreRefused()
        {
            var e = Assert.ThrowsExactly<ArgumentException>(
                () => Serializer.ToByteArray(new TwoCases { Value = 1, value = 2 }));
            StringAssert.Contains(e.Message, "'Value' and 'value' differ only in case");
        }

        /// <summary>
        ///     A clash between a derived and a base type's member is refused as well.
        /// </summary>
        [TestMethod]
        public void CaseOnlyDifferentAcrossTheHierarchyIsRefused()
        {
            Assert.ThrowsExactly<ArgumentException>(() => Serializer.ToByteArray(new DerivedClash { Name = "a", name = "b" }));
        }

        /// <summary>
        ///     A member hidden with <c>new</c> under the same name is not a clash: the walk over the
        ///     base types meets that name again exactly, and the derived member is the one kept.
        /// </summary>
        [TestMethod]
        public void SameNameInTheHierarchyIsNotAClash()
        {
            var value = new DerivedHiding { Name = "derived" };
            var copy = Deserializer.FromByteArray<DerivedHiding>(Serializer.ToByteArray(value));
            Assert.AreEqual("derived", copy.Name);
        }

        /// <summary>
        ///     An ordinary type is unaffected: every member round-trips.
        /// </summary>
        [TestMethod]
        public void DistinctNamesAllRoundTrip()
        {
            var value = new DistinctNames { Alpha = 1, Beta = "two", Gamma = 3.0 };
            var copy = Deserializer.FromByteArray<DistinctNames>(Serializer.ToByteArray(value));
            Assert.AreEqual(1, copy.Alpha);
            Assert.AreEqual("two", copy.Beta);
            Assert.AreEqual(3.0, copy.Gamma);
        }

        public sealed class TwoCases
        {
            public int Value { get; set; }

#pragma warning disable IDE1006 // the lower-case name is the point of the test
            public int value { get; set; }
#pragma warning restore IDE1006
        }

        public class ClashBase
        {
            public string Name { get; set; }
        }

        public sealed class DerivedClash : ClashBase
        {
#pragma warning disable IDE1006 // the lower-case name is the point of the test
            public string name { get; set; }
#pragma warning restore IDE1006
        }

        public class HidingBase
        {
            public string Name { get; set; }
        }

        public sealed class DerivedHiding : HidingBase
        {
            public new string Name { get; set; }
        }

        public sealed class DistinctNames
        {
            public int Alpha { get; set; }

            public string Beta { get; set; }

            public double Gamma { get; set; }
        }
    }
}
