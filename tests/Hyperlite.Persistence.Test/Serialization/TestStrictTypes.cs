namespace Hyperlite.Persistence.Test.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    using Hyperlite.Persistence.Serialization;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using SerializationException = Hyperlite.Persistence.Serialization.SerializationException;

    /// <summary>
    ///     Strict types: reading creates only what the application declared or allowed.
    /// </summary>
    /// <remarks>
    ///     On by default. Off, reading creates whatever type the bytes name. On, a type that was neither
    ///     declared - the type asked for and what its members declare - nor allowed is refused
    ///     before anything is created from it. <c>Gadget</c> counts its constructions, which is
    ///     how the tests see that a refused type had none of its code run.
    /// </remarks>
    [TestClass]
    public sealed class TestStrictTypes
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            InvariantCulture.Pin();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            Encoder.Configuration.StrictTypes = true;
        }

        [TestMethod]
        public void StrictTypesAreTheDefault()
        {
            Assert.IsTrue(new EncoderConfiguration().StrictTypes);
            Assert.IsTrue(Encoder.Configuration.StrictTypes);
        }

        /// <summary>
        ///     A graph the application declared reads as it did: nested types, collections,
        ///     arrays, enums and the framework's value types.
        /// </summary>
        [TestMethod]
        public void DeclaredGraphRoundTrips()
        {
            var when = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            var id = Guid.NewGuid();
            var bytes = Serializer.ToByteArray(new Order
            {
                Number = 7,
                Kind = OrderKind.Express,
                When = when,
                Address = new Address { City = "Bern" },
                Lines = new List<Line> { new Line { Sku = "a", Quantity = 2 } },
                Tags = new Dictionary<string, int> { { "x", 1 } },
                Codes = new[] { 1, 2 },
                Ids = new HashSet<Guid> { id }
            });

            Encoder.Configuration.StrictTypes = true;
            var copy = Deserializer.FromByteArray<Order>(bytes);

            Assert.AreEqual(7, copy.Number);
            Assert.AreEqual(OrderKind.Express, copy.Kind);
            Assert.AreEqual(when, copy.When);
            Assert.AreEqual("Bern", copy.Address.City);
            Assert.AreEqual(2, copy.Lines[0].Quantity);
            Assert.AreEqual(1, copy.Tags["x"]);
            CollectionAssert.AreEqual(new[] { 1, 2 }, copy.Codes);
            Assert.IsTrue(copy.Ids.Contains(id));
        }

        /// <summary>
        ///     A type the bytes name but the application never declared is refused, and none of
        ///     its code runs.
        /// </summary>
        [TestMethod]
        public void AnUndeclaredTypeIsRefusedBeforeItIsCreated()
        {
            AssertRefused(new Holder { Payload = new Gadget() });
        }

        /// <summary>
        ///     An allowed generic type does not carry an undeclared argument in with it.
        /// </summary>
        [TestMethod]
        public void AnUndeclaredTypeArgumentIsRefused()
        {
            AssertRefused(new Holder { Payload = new List<Gadget> { new Gadget() } });
        }

        /// <summary>
        ///     An array is checked by its element type.
        /// </summary>
        [TestMethod]
        public void AnUndeclaredElementTypeIsRefused()
        {
            AssertRefused(new Holder { Payload = new[] { new Gadget() } });
        }

        /// <summary>
        ///     An ISerializable type is refused before its deserialization constructor runs.
        /// </summary>
        [TestMethod]
        public void AnUndeclaredSerializableTypeIsRefusedBeforeItsConstructorRuns()
        {
            AssertRefused(new Holder { Payload = new SerializableGadget() });
        }

        /// <summary>
        ///     A value of type Type is checked as well: code that reads it may well create one.
        /// </summary>
        [TestMethod]
        public void ATypeValueNamingAnUndeclaredTypeIsRefused()
        {
            var bytes = Serializer.ToByteArray(new Holder { Payload = typeof(Gadget) });

            Encoder.Configuration.StrictTypes = true;
            var e = Assert.ThrowsExactly<SerializationException>(() => Deserializer.FromByteArray<Holder>(bytes));
            StringAssert.Contains(e.Message, nameof(Gadget));
        }

        /// <summary>
        ///     A type allowed explicitly is read, wherever it is stored.
        /// </summary>
        [TestMethod]
        public void AnAllowedTypeIsRead()
        {
            var bytes = Serializer.ToByteArray(new Holder { Payload = new AllowedPayload { Value = 3 } });

            Encoder.Configuration.StrictTypes = true;
            Encoder.Configuration.AllowType(typeof(AllowedPayload));
            var copy = Deserializer.FromByteArray<Holder>(bytes);

            Assert.AreEqual(3, ((AllowedPayload)copy.Payload).Value);
        }

        /// <summary>
        ///     Switched off, reading creates what the bytes name, as it did before strict types.
        /// </summary>
        [TestMethod]
        public void OffReadsWhatTheBytesName()
        {
            var bytes = Serializer.ToByteArray(new Holder { Payload = new Gadget() });
            Gadget.Constructed = 0;

            Encoder.Configuration.StrictTypes = false;

            var copy = Deserializer.FromByteArray<Holder>(bytes);

            Assert.IsInstanceOfType(copy.Payload, typeof(Gadget));
            Assert.IsTrue(Gadget.Constructed > 0);
        }

        /// <summary>
        ///     A stored name never makes strict reading load an assembly: loading runs the
        ///     assembly's code before anything could be refused.
        /// </summary>
        /// <remarks>
        ///     The runtime raises AssemblyResolve for every assembly it tries and fails to load,
        ///     so a name of one that does not exist shows whether a load was attempted. With
        ///     strict types off one is, which is the control - and since a name that does not
        ///     resolve is no longer cached, the second read attempts it again.
        /// </remarks>
        [TestMethod]
        public void AStoredNameNeverLoadsAnAssembly()
        {
            byte[] bytes;
            using (var stream = new System.IO.MemoryStream())
            {
                new Serializer(new EncoderConfiguration { Binder = new NamesNoLoadedAssembly() })
                    .Serialize(stream, new Holder { Payload = new AllowedPayload { Value = 1 } });
                bytes = stream.ToArray();
            }

            var attempts = 0;
            ResolveEventHandler count = (sender, args) =>
                {
                    if (args.Name.StartsWith(NamesNoLoadedAssembly.Assembly, StringComparison.Ordinal))
                    {
                        ++attempts;
                    }

                    return null;
                };
            AppDomain.CurrentDomain.AssemblyResolve += count;
            try
            {
                // the type resolves either way to nothing, and reading says so; what differs is
                // whether getting there tried to load an assembly
                Encoder.Configuration.StrictTypes = true;
                Assert.ThrowsExactly<SerializationException>(() => Deserializer.FromByteArray<Holder>(bytes));
                Assert.AreEqual(0, attempts, "strict reading tried to load the assembly a stored name names");

                Encoder.Configuration.StrictTypes = false;
                Assert.ThrowsExactly<SerializationException>(() => Deserializer.FromByteArray<Holder>(bytes));
                Assert.IsTrue(attempts > 0, "the control: reading with strict types off tries to load it");
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= count;
            }
        }

        /// <summary>
        ///     A name that does not resolve is not cached: the names come from the bytes, and
        ///     caching every miss grew the cache with every distinct name for the life of the
        ///     process.
        /// </summary>
        [TestMethod]
        public void ANameThatDoesNotResolveIsNotCached()
        {
            var cache = (System.Collections.ICollection)typeof(Hyperlite.Persistence.Reflection.TypeLoader)
                .GetField("Types", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);
            var before = cache.Count;

            for (var i = 0; i < 1000; ++i)
            {
                Assert.IsNull(Hyperlite.Persistence.Reflection.TypeLoader.GetType("No.Such.Type" + i + ", No.Such.Assembly"));
            }

            Assert.AreEqual(before, cache.Count, "names that did not resolve were cached");
        }

        private static void AssertRefused(Holder value)
        {
            var bytes = Serializer.ToByteArray(value);
            Gadget.Constructed = 0;
            SerializableGadget.Constructed = 0;

            Encoder.Configuration.StrictTypes = true;
            var e = Assert.ThrowsExactly<SerializationException>(() => Deserializer.FromByteArray<Holder>(bytes));

            StringAssert.Contains(e.Message, "Gadget");
            Assert.AreEqual(0, Gadget.Constructed, "a refused type was constructed");
            Assert.AreEqual(0, SerializableGadget.Constructed, "a refused type was constructed");
        }

        public enum OrderKind
        {
            Standard,

            Express
        }

        public sealed class Order
        {
            public int Number { get; set; }

            public OrderKind Kind { get; set; }

            public DateTime When { get; set; }

            public Address Address { get; set; }

            public List<Line> Lines { get; set; }

            public Dictionary<string, int> Tags { get; set; }

            public int[] Codes { get; set; }

            public HashSet<Guid> Ids { get; set; }
        }

        public sealed class Address
        {
            public string City { get; set; }
        }

        public sealed class Line
        {
            public string Sku { get; set; }

            public int Quantity { get; set; }
        }

        public sealed class Holder
        {
            public object Payload { get; set; }
        }

        /// <summary>
        ///     Stands in for a type whose constructor does something.
        /// </summary>
        public sealed class Gadget
        {
            public Gadget()
            {
                ++Constructed;
            }

            public static int Constructed { get; set; }

            public string Name { get; set; }
        }

        [Serializable]
        public sealed class SerializableGadget : ISerializable
        {
            public SerializableGadget()
            {
            }

            private SerializableGadget(SerializationInfo info, StreamingContext context)
            {
                ++Constructed;
            }

            public static int Constructed { get; set; }

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.AddValue("n", 1);
            }
        }

        public sealed class AllowedPayload
        {
            public int Value { get; set; }
        }

        /// <summary>
        ///     Writes AllowedPayload under a name in an assembly that does not exist.
        /// </summary>
        private sealed class NamesNoLoadedAssembly : Binder
        {
            internal const string Assembly = "Hyperlite.NoSuch.Assembly";

            public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                base.BindToName(serializedType, out assemblyName, out typeName);
                if (serializedType == typeof(AllowedPayload))
                {
                    assemblyName = Assembly;
                    typeName = "Hyperlite.NoSuch.Type";
                }
            }
        }
    }
}
