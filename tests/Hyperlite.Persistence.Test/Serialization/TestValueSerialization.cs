namespace Hyperlite.Persistence.Test.Serialization
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text;
    using System.Xml;

    using Hyperlite.Persistence.Serialization;
    using Hyperlite.Persistence.Test.Serialization.SampleTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using Encoder = Hyperlite.Persistence.Serialization.Encoder;

    /// <summary>
    ///     Scalars: the primitives, their nullable forms, enums and the date and time types.
    /// </summary>
    [TestClass]
    public sealed class TestValueSerialization
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            InvariantCulture.Pin();

            // strict types: these tests write Type values naming types nothing here declares
            Encoder.Configuration.AllowType(typeof(Assert));
            Encoder.Configuration.AllowType(typeof(XmlText));
        }

        /// <summary>
        ///     Each primitive type round-trips through a byte array.
        /// </summary>
        [TestMethod]
        public void Primitives()
        {
            var b = Serializer.ToByteArray(true);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<bool>(b));

            b = Serializer.ToByteArray(false);
            Assert.IsNotNull(b);
            Assert.IsFalse(Deserializer.FromByteArray<bool>(b));

            b = Serializer.ToByteArray<byte>(0);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<byte>(b) == 0);

            b = Serializer.ToByteArray<byte>(0x34);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<byte>(b) == 0x34);
            Assert.IsTrue(Deserializer.FromByteArray<sbyte>(b) == 0x34);

            b = Serializer.ToByteArray<sbyte>(0);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<sbyte>(b) == 0);

            b = Serializer.ToByteArray<sbyte>(-4);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<sbyte>(b) == -4);

            b = Serializer.ToByteArray<short>(0);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<short>(b) == 0);

            b = Serializer.ToByteArray<short>(-1024);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<short>(b) == -1024);

            b = Serializer.ToByteArray<ushort>(0);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<ushort>(b) == 0);

            b = Serializer.ToByteArray<ushort>(1024);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<ushort>(b) == 1024);
            Assert.IsTrue(Deserializer.FromByteArray<short>(b) == 1024);

            b = Serializer.ToByteArray(0);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<int>(b) == 0);

            b = Serializer.ToByteArray(-2048);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<int>(b) == -2048);

            b = Serializer.ToByteArray<uint>(0);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<uint>(b) == 0);

            b = Serializer.ToByteArray<uint>(2048);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<uint>(b) == 2048);
            Assert.IsTrue(Deserializer.FromByteArray<int>(b) == 2048);

            b = Serializer.ToByteArray<long>(0);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<long>(b) == 0);

            b = Serializer.ToByteArray<long>(-4096);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<long>(b) == -4096);

            b = Serializer.ToByteArray<ulong>(0);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<ulong>(b) == 0);

            b = Serializer.ToByteArray<ulong>(4096);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<ulong>(b) == 4096);
            Assert.IsTrue(Deserializer.FromByteArray<long>(b) == 4096);

            b = Serializer.ToByteArray(0.0f);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<float>(b) == 0.0f);

            b = Serializer.ToByteArray(1.234234f);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<float>(b) == 1.234234f);

            b = Serializer.ToByteArray(5.0f);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<float>(b) == 5.0f);

            b = Serializer.ToByteArray(0.0);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<double>(b) == 0.0);

            b = Serializer.ToByteArray(1.234234);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<double>(b) == 1.234234);

            b = Serializer.ToByteArray(5.0);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<double>(b) == 5.0);

            b = Serializer.ToByteArray(5.1111m);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<decimal>(b) == 5.1111m);

            b = Serializer.ToByteArray(-5.1111m);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<decimal>(b) == -5.1111m);

            b = Serializer.ToByteArray(string.Empty);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<string>(b) == string.Empty);

            b = Serializer.ToByteArray("abcdefg");
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<string>(b) == "abcdefg");
            Assert.IsTrue(Deserializer.FromByteArray<StringBuilder>(b).ToString() == "abcdefg");

            var largeString = new string('x', 256);
            b = Serializer.ToByteArray(largeString);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<string>(b) == largeString);
            Assert.IsTrue(Deserializer.FromByteArray<StringBuilder>(b).ToString() == largeString);

            largeString = string.Concat(new string('x', 256), new string('y', 256));
            b = Serializer.ToByteArray(largeString);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<string>(b) == largeString);
            Assert.IsTrue(Deserializer.FromByteArray<StringBuilder>(b).ToString() == largeString);

            largeString = string.Concat(new string('x', 256), new string('y', 256), new string('z', 256));
            b = Serializer.ToByteArray(largeString);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<string>(b) == largeString);
            Assert.IsTrue(Deserializer.FromByteArray<StringBuilder>(b).ToString() == largeString);

            b = Serializer.ToByteArray<string>(null);
            Assert.IsNotNull(b);
            Assert.IsNull(Deserializer.FromByteArray<string>(b));
            Assert.IsNull(Deserializer.FromByteArray<StringBuilder>(b));

            b = Serializer.ToByteArray(new StringBuilder("abcdefg"));
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<StringBuilder>(b).ToString() == "abcdefg");
            Assert.IsTrue(Deserializer.FromByteArray<string>(b) == "abcdefg");

            b = Serializer.ToByteArray<StringBuilder>(null);
            Assert.IsNotNull(b);
            Assert.IsNull(Deserializer.FromByteArray<StringBuilder>(b));
            Assert.IsNull(Deserializer.FromByteArray<string>(b));

            var guid = Guid.NewGuid();
            b = Serializer.ToByteArray(guid);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<Guid>(b).Equals(guid));

            var type = typeof(string);
            b = Serializer.ToByteArray(type);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<Type>(b) == type);

            type = typeof(Dictionary<int, Uri>);
            b = Serializer.ToByteArray(type);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<Type>(b) == type);

            type = typeof(Assert);
            b = Serializer.ToByteArray(type);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<Type>(b) == type);

            type = typeof(XmlText);
            b = Serializer.ToByteArray(type);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<Type>(b) == type);

            var uri = new Uri("http://www.xyz.com/path/local?attr=50;val='abc'");
            b = Serializer.ToByteArray(uri);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<Uri>(b).Equals(uri));
        }

        /// <summary>
        ///     The nullable form of each primitive round-trips, null and not.
        /// </summary>
        [TestMethod]
        public void NullablePrimitives()
        {
            {
                bool? bv = null;
                var b = Serializer.ToByteArray(bv);
                Assert.IsNotNull(b);
                bv = Deserializer.FromByteArray<bool?>(b);
                Assert.IsFalse(bv.HasValue);

                bv = true;
                b = Serializer.ToByteArray(bv);
                Assert.IsNotNull(b);
                bv = Deserializer.FromByteArray<bool?>(b);
                Assert.IsTrue(bv.HasValue);
                Assert.IsTrue(bv.Value);

                bv = false;
                b = Serializer.ToByteArray(bv);
                Assert.IsNotNull(b);
                bv = Deserializer.FromByteArray<bool?>(b);
                Assert.IsTrue(bv.HasValue);
                Assert.IsFalse(bv.Value);
            }

            {
                byte? bv = null;
                var b = Serializer.ToByteArray(bv);
                Assert.IsNotNull(b);
                bv = Deserializer.FromByteArray<byte?>(b);
                Assert.IsFalse(bv.HasValue);

                bv = 0x34;
                b = Serializer.ToByteArray(bv);
                Assert.IsNotNull(b);
                bv = Deserializer.FromByteArray<byte?>(b);
                Assert.IsTrue(bv.HasValue);
                Assert.IsTrue(bv.Value == 0x34);
                Assert.IsTrue(Deserializer.FromByteArray<byte>(b) == 0x34);
                Assert.IsTrue(Deserializer.FromByteArray<sbyte>(b) == 0x34);
            }
        }

        /// <summary>
        ///     Enum values round-trip.
        /// </summary>
        [TestMethod]
        public void Enums()
        {
            var b = Serializer.ToByteArray(Numbers.One);
            Assert.IsNotNull(b);
            Assert.AreEqual(Numbers.One, Deserializer.FromByteArray<Numbers>(b));

            b = Serializer.ToByteArray(Numbers.Two);
            Assert.IsNotNull(b);
            Assert.AreEqual(Numbers.Two, Deserializer.FromByteArray<Numbers>(b));

            b = Serializer.ToByteArray(Numbers.Three);
            Assert.IsNotNull(b);
            Assert.AreEqual(Numbers.Three, Deserializer.FromByteArray<Numbers>(b));

            b = Serializer.ToByteArray(Numbers.Three);
            Assert.IsNotNull(b);
            Assert.AreEqual(2, Deserializer.FromByteArray<object>(b));
        }

        /// <summary>
        ///     DateTime values round-trip.
        /// </summary>
        [TestMethod]
        public void DateTimes()
        {
            var dt = new DateTime();
            var b = Serializer.ToByteArray(dt);
            Assert.IsNotNull(b);
            var dtr = Deserializer.FromByteArray<DateTime>(b);
            Assert.IsTrue(dtr == dt && dtr.Kind == dt.Kind);

            dt = DateTime.Now;
            b = Serializer.ToByteArray(dt);
            Assert.IsNotNull(b);
            dtr = Deserializer.FromByteArray<DateTime>(b);
            Assert.IsTrue(dtr == dt && dtr.Kind == dt.Kind);

            dt = DateTime.UtcNow;
            b = Serializer.ToByteArray(dt);
            Assert.IsNotNull(b);
            dtr = Deserializer.FromByteArray<DateTime>(b);
            Assert.IsTrue(dtr == dt && dtr.Kind == dt.Kind);

            dt = DateTime.MaxValue;
            b = Serializer.ToByteArray(dt);
            Assert.IsNotNull(b);
            dtr = Deserializer.FromByteArray<DateTime>(b);
            Assert.IsTrue(dtr == dt && dtr.Kind == dt.Kind);

            dt = DateTime.MinValue;
            b = Serializer.ToByteArray(dt);
            Assert.IsNotNull(b);
            dtr = Deserializer.FromByteArray<DateTime>(b);
            Assert.IsTrue(dtr == dt && dtr.Kind == dt.Kind);

            dt = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
            b = Serializer.ToByteArray(dt);
            Assert.IsNotNull(b);
            dtr = Deserializer.FromByteArray<DateTime>(b);
            Assert.IsTrue(dtr == dt && dtr.Kind == dt.Kind);
        }

        /// <summary>
        ///     DateTimeOffset values round-trip.
        /// </summary>
        [TestMethod]
        public void DateTimeOffsets()
        {
            var dto = new DateTimeOffset();
            var b = Serializer.ToByteArray(dto);
            Assert.IsNotNull(b);
            var dtor = Deserializer.FromByteArray<DateTimeOffset>(b);
            Assert.IsTrue(dtor == dto && dtor.Offset == dto.Offset);

            dto = DateTimeOffset.Now;
            b = Serializer.ToByteArray(dto);
            Assert.IsNotNull(b);
            dtor = Deserializer.FromByteArray<DateTimeOffset>(b);
            Assert.IsTrue(dtor == dto && dtor.Offset == dto.Offset);

            dto = DateTimeOffset.UtcNow;
            b = Serializer.ToByteArray(dto);
            Assert.IsNotNull(b);
            dtor = Deserializer.FromByteArray<DateTimeOffset>(b);
            Assert.IsTrue(dtor == dto && dtor.Offset == dto.Offset);

            dto = DateTimeOffset.MaxValue;
            b = Serializer.ToByteArray(dto);
            Assert.IsNotNull(b);
            dtor = Deserializer.FromByteArray<DateTimeOffset>(b);
            Assert.IsTrue(dtor == dto && dtor.Offset == dto.Offset);

            dto = DateTimeOffset.MinValue;
            b = Serializer.ToByteArray(dto);
            Assert.IsNotNull(b);
            dtor = Deserializer.FromByteArray<DateTimeOffset>(b);
            Assert.IsTrue(dtor == dto && dtor.Offset == dto.Offset);

            dto = new DateTimeOffset(DateTime.Now.Ticks, TimeSpan.FromHours(4));
            b = Serializer.ToByteArray(dto);
            Assert.IsNotNull(b);
            dtor = Deserializer.FromByteArray<DateTimeOffset>(b);
            Assert.IsTrue(dtor == dto && dtor.Offset == dto.Offset);
        }

        /// <summary>
        ///     TimeSpan values round-trip.
        /// </summary>
        [TestMethod]
        public void TimeSpans()
        {
            var ts = new TimeSpan();
            var b = Serializer.ToByteArray(ts);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<TimeSpan>(b) == ts);

            ts = TimeSpan.FromMilliseconds(324235.2353);
            b = Serializer.ToByteArray(ts);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<TimeSpan>(b) == ts);

            ts = TimeSpan.MaxValue;
            b = Serializer.ToByteArray(ts);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<TimeSpan>(b) == ts);

            ts = TimeSpan.MinValue;
            b = Serializer.ToByteArray(ts);
            Assert.IsNotNull(b);
            Assert.IsTrue(Deserializer.FromByteArray<TimeSpan>(b) == ts);
        }
    }
}
