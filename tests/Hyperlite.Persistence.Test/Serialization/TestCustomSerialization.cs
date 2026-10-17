namespace Hyperlite.Persistence.Test.Serialization
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text;
    using System.Xml;

    using Hyperlite.Persistence.Serialization;
    using Hyperlite.Persistence.Test.Common;
    using Hyperlite.Persistence.Test.Serialization.SampleTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using Decoder = Hyperlite.Persistence.Serialization.Decoder;
    using Encoder = Hyperlite.Persistence.Serialization.Encoder;

    /// <summary>
    ///     Types that take part in their own serialization.
    /// </summary>
    /// <remarks>
    ///     ISerializable, the callback attributes, exceptions, and a type with an encoder and
    ///     decoder registered for it. What these have in common is that the form on the wire is
    ///     not derived from the type's properties, so a change to the serializer can leave the
    ///     rest of this suite passing and still break them.
    /// </remarks>
    [TestClass]
    public sealed class TestCustomSerialization
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            InvariantCulture.Pin();
        }

        /// <summary>
        ///     A type implementing ISerializable controls its own form.
        /// </summary>
        [TestMethod]
        public void Serializables()
        {
            {
                var sA = new ObjectSerializableA { X = 1.9, Y = 23.4545, Z = 3535.35, Name = "abc" };
                var b = Serializer.ToByteArray(sA);
                Assert.IsNotNull(b);
                sA.AssertIsEqualObjectSerializableA(Deserializer.FromByteArray<ObjectSerializableA>(b));
                sA.AssertIsEqualObjectSerializableA(Deserializer.FromByteArray<object>(b));
                sA.AssertIsEqualStructA(Deserializer.FromByteArray<StructA>(b));
                sA.AssertIsEqualObjectA_Nullable(Deserializer.FromByteArray<StructA_Nullable>(b));
            }
        }

        /// <summary>
        ///     The serialization callback attributes are invoked.
        /// </summary>
        [TestMethod]
        public void SerializationCallbacks()
        {
            // off by default
            Encoder.Configuration.EnableSerializationCallbacks = true;
            try
            {
                var o = new ObjectSerializableAttributes();
                var b = Serializer.ToByteArray(o);
                Assert.IsNotNull(b);
                Assert.AreEqual(1, o.OnSerializing);
                Assert.AreEqual(2, o.OnSerialized);
                Assert.AreEqual(0, o.OnDeserializing);
                Assert.AreEqual(0, o.OnDeserialized);

                var r = Deserializer.FromByteArray<ObjectSerializableAttributes>(b);
                Assert.IsNotNull(r);
                Assert.AreEqual(0, r.OnSerializing);
                Assert.AreEqual(0, r.OnSerialized);
                Assert.AreEqual(1, r.OnDeserializing);
                Assert.AreEqual(2, r.OnDeserialized);
            }
            finally
            {
                Encoder.Configuration.EnableSerializationCallbacks = false;
            }
        }

        /// <summary>
        ///     An exception round-trips.
        /// </summary>
        [TestMethod]
        public void Exceptions()
        {
            var e = new Exception("Exception");
            try
            {
                throw e;
            }
            catch (Exception ec)
            {
                var b = Serializer.ToByteArray(ec);
                Assert.IsNotNull(b);
                var eb = Deserializer.FromByteArray<Exception>(b);

                Assert.AreEqual(e.Message, eb.Message);
                Assert.AreEqual(ec.StackTrace, eb.StackTrace);
            }
        }

        /// <summary>
        ///     A type with an encoder and decoder of its own round-trips through them.
        /// </summary>
        [TestMethod]
        public void CustomEncoderAndDecoder()
        {
            Encoder.Register(
                0,
                typeof(CustomTypeA),
                (serializer, any) =>
                    {
                        var binaryWriter = serializer.BinaryWriter;
                        var cta = (CustomTypeA)any;
                        Encoder.WriteDouble(binaryWriter, cta.X, false);
                        Encoder.WriteInt(binaryWriter, cta.Y, false);
                        Encoder.WriteLong(binaryWriter, cta.Z, false);
                    },
                (deserializer) =>
                    {
                        var binaryReader = deserializer.BinaryReader;
                        return new CustomTypeA { X = Decoder.ReadDouble(binaryReader), Y = Decoder.ReadInt(binaryReader), Z = Decoder.ReadLong(binaryReader) };
                    });



            Encoder.Register(
                1,
                typeof(CustomNumbers),
                (serializer, any) =>
                {
                    var bw = serializer.BinaryWriter;
                    var e = (CustomNumbers)any;
                    Encoder.WriteByte(bw, (byte)e, false);
                },
                deserializer =>
                {
                    var br = deserializer.BinaryReader;
                    return (CustomNumbers)Decoder.ReadByte(br);
                });

            {
                var sA = new CustomTypeA { X = 1.9, Y = 234, Z = 35354634635L };
                var b = Serializer.ToByteArray(sA);
                Assert.IsNotNull(b);
                sA.AssertIsEqualCustomTypeA(Deserializer.FromByteArray<CustomTypeA>(b));
            }

            {
                var sA = new CustomTypeA { X = 1.9, Y = 234, Z = 35354634635L };
                var b = Serializer.ToByteArray(sA);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<object>(b);
                Assert.IsInstanceOfType(d, typeof(CustomTypeA));
                sA.AssertIsEqualCustomTypeA((CustomTypeA)d);
            }

            {
                var av = new[] { new CustomTypeA { X = 1.9, Y = 234545, Z = 3545764535L }, new CustomTypeA { X = 2.9, Y = 5345, Z = 457535 } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<CustomTypeA[]>(b);
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }

            {
                var av = new[] { new CustomTypeA { X = 1.9, Y = 234545, Z = 3545764535L }, new CustomTypeA { X = 2.9, Y = 5345, Z = 457535 } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<object>(b);
                Assert.IsInstanceOfType(d, typeof(CustomTypeA[]));
                var avr = (CustomTypeA[])d;
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }

            {
                var av = new List<CustomTypeA> { new CustomTypeA { X = 1.9, Y = 234545, Z = 3545764535L }, new CustomTypeA { X = 2.9, Y = 5345, Z = 457535 } };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var avr = Deserializer.FromByteArray<List<CustomTypeA>>(b);
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }

            {
                var b = Serializer.ToByteArray(CustomNumbers.Three);
                Assert.IsNotNull(b);
                Assert.AreEqual(CustomNumbers.Three, Deserializer.FromByteArray<object>(b));
            }

            {
                var av = new[] { CustomNumbers.One, CustomNumbers.Three };
                var b = Serializer.ToByteArray(av);
                Assert.IsNotNull(b);
                var d = Deserializer.FromByteArray<object>(b);
                Assert.IsInstanceOfType(d, typeof(CustomNumbers[]));
                var avr = (CustomNumbers[])d;
                Assert.IsTrue(Equatable.AreEqual((object)av, avr));
            }
        }
    }
}
