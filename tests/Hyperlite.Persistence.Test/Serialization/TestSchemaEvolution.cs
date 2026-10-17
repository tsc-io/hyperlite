namespace Hyperlite.Persistence.Test.Serialization
{
    using System;
    using System.IO;
    using System.Runtime.Serialization;

    using Hyperlite.Persistence.Serialization;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     A class that changed between writing and reading reads what still fits - the claims
    ///     doc/serialization.md makes, pinned.
    /// </summary>
    /// <remarks>
    ///     One class cannot have two versions in one process, so the older version is a class
    ///     of its own, written through a binder that records it under the newer one's name -
    ///     which is exactly what the newer one meets when it reads what an older build wrote.
    /// </remarks>
    [TestClass]
    public sealed class TestSchemaEvolution
    {
        private Action<object, string, object> obsoletePropertyResolver;

        private Func<Type, string, string> propertyNameResolver;

        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            InvariantCulture.Pin();
        }

        [TestInitialize]
        public void TestInitialize()
        {
            this.obsoletePropertyResolver = Resolver.ObsoletePropertyResolver;
            this.propertyNameResolver = Resolver.PropertyNameResolver;
        }

        [TestCleanup]
        public void TestCleanup()
        {
            Resolver.ObsoletePropertyResolver = this.obsoletePropertyResolver;
            Resolver.PropertyNameResolver = this.propertyNameResolver;
        }

        /// <summary>
        ///     Members added, widened, retyped, renamed with DataMember and removed, at once.
        /// </summary>
        [TestMethod]
        public void ANewerClassReadsWhatAnOlderOneWrote()
        {
            string obsolete = null;
            Resolver.ObsoletePropertyResolver = (target, name, value) => obsolete = name + "=" + value;

            var bytes = WrittenAs<Version2>(new Version1 { Count = 7, Name = "seven", Kind = Kind.B, Retired = "gone" });
            var read = Deserializer.FromByteArray<Version2>(bytes);

            Assert.AreEqual(7L, read.Count, "an int member widened to long");
            Assert.AreEqual("seven", read.Title, "a member renamed, keeping its stored name with DataMember");
            Assert.AreEqual(2, read.Kind, "an enum member now an int");
            Assert.AreEqual(new DateTime(2026, 1, 1), read.Added, "a member added keeps its default");
            Assert.AreEqual("Retired=gone", obsolete, "a member removed reaches the obsolete resolver");
        }

        /// <summary>
        ///     A member renamed without DataMember: the resolver maps the stored name.
        /// </summary>
        [TestMethod]
        public void TheResolverMapsARenamedMember()
        {
            Resolver.PropertyNameResolver = (type, stored) => type == typeof(Renamed) && stored == "Count" ? "Total" : null;

            var read = Deserializer.FromByteArray<Renamed>(WrittenAs<Renamed>(new Version1 { Count = 9 }));

            Assert.AreEqual(9, read.Total);
        }

        /// <summary>
        ///     A name the resolver mapped is not recorded on the type.
        /// </summary>
        /// <remarks>
        ///     It was: the alias went in as a second key of the type's members, whose values are
        ///     what a type schema is made of - so the type, written after the alias, wrote that
        ///     member twice. Its twin, identical but never read through an alias, is the measure.
        ///     The record also grew with every distinct stored name, and was written under a lock
        ///     while other threads read it without one.
        /// </remarks>
        [TestMethod]
        public void AResolvedNameIsNotRecordedOnTheType()
        {
            Resolver.PropertyNameResolver = (type, stored) => type == typeof(AliasA) && stored == "Count" ? "Total" : null;
            Assert.AreEqual(5, Deserializer.FromByteArray<AliasA>(WrittenAs<AliasA>(new Version1 { Count = 5 })).Total);

            var aliased = Serializer.ToByteArray(new AliasA { Total = 3 });
            var twin = Serializer.ToByteArray(new AliasB { Total = 3 });

            Assert.AreEqual(twin.Length, aliased.Length, "the type wrote a member twice after an alias was resolved");
            Assert.AreEqual(3, Deserializer.FromByteArray<AliasA>(aliased).Total);
        }

        /// <summary>
        ///     A member whose name is not ascii reads back. The schema recorded its name in ascii,
        ///     a "?" for every other char: nothing matched on reading, the value went to the
        ///     obsolete property resolver, and Größe and Grüße were the same name.
        /// </summary>
        [TestMethod]
        public void AMemberWithANonAsciiNameReadsBack()
        {
            string obsolete = null;
            Resolver.ObsoletePropertyResolver = (target, name, value) => obsolete = name;

            var read = Deserializer.FromByteArray<Accented>(Serializer.ToByteArray(new Accented { Size = 1, Greeting = "hi" }));

            Assert.IsNull(obsolete, "a member was taken for an obsolete one");
            Assert.AreEqual(1, read.Size);
            Assert.AreEqual("hi", read.Greeting);
        }

        /// <summary>
        ///     Writes a value under another type's name, as an older build of that type would have.
        /// </summary>
        private static byte[] WrittenAs<T>(Version1 value)
        {
            using (var stream = new MemoryStream())
            {
                new Serializer(new EncoderConfiguration { Binder = new WriteAs(typeof(T)) }).Serialize(stream, value);
                return stream.ToArray();
            }
        }

        public enum Kind
        {
            A = 1,

            B = 2
        }

        /// <summary>
        ///     The older version.
        /// </summary>
        public sealed class Version1
        {
            public int Count { get; set; }

            public string Name { get; set; }

            public Kind Kind { get; set; }

            public string Retired { get; set; }
        }

        /// <summary>
        ///     The newer version: Count widened, Name renamed to Title, Kind an int, Retired
        ///     removed, Added new.
        /// </summary>
        public sealed class Version2
        {
            public long Count { get; set; }

            [DataMember(Name = "Name")]
            public string Title { get; set; }

            public int Kind { get; set; }

            public DateTime Added { get; set; } = new DateTime(2026, 1, 1);
        }

        public sealed class Renamed
        {
            public int Total { get; set; }
        }

        public sealed class Accented
        {
            [DataMember(Name = "Größe")]
            public int Size { get; set; }

            [DataMember(Name = "Grüße")]
            public string Greeting { get; set; }
        }

        public sealed class AliasA
        {
            public int Total { get; set; }
        }

        public sealed class AliasB
        {
            public int Total { get; set; }
        }

        private sealed class WriteAs : Binder
        {
            private readonly Type type;

            public WriteAs(Type type)
            {
                this.type = type;
            }

            public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                base.BindToName(serializedType == typeof(Version1) ? this.type : serializedType, out assemblyName, out typeName);
            }
        }
    }
}
