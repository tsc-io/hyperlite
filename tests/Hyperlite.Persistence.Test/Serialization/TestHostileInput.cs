namespace Hyperlite.Persistence.Test.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    using Hyperlite.Persistence.Serialization;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Input that is nested too deeply or states more than it holds is refused - with an
    ///     exception, before the damage, rather than with a dead process or gigabytes allocated.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Reading recursed once per level, and about 30 KB of nested arrays overflowed the
    ///         stack, which .NET cannot catch: the process ended. And a count was allocated
    ///         before its items were read, so seven bytes asked for 200 MB. Both were measured
    ///         before they were fixed.
    ///     </para>
    ///     <para>
    ///         The crafted inputs are made from what the serializer writes, with the one field
    ///         under test changed, so the tests do not hard-code the format.
    ///     </para>
    /// </remarks>
    [TestClass]
    public sealed class TestHostileInput
    {
        /// <summary>
        ///     Far deeper than any stack holds.
        /// </summary>
        private const int TooDeep = 100000;

        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            InvariantCulture.Pin();
        }

        /// <summary>
        ///     Nesting too deep to read throws instead of overflowing the stack.
        /// </summary>
        [TestMethod]
        [Timeout(120000)]
        public void NestingTooDeepToReadThrows()
        {
            var bytes = WrittenOnALargeStack(TooDeep);
            var e = Assert.ThrowsExactly<SerializationException>(() => Deserializer.FromByteArray<object>(bytes));
            StringAssert.Contains(e.Message, "nested too deeply");
        }

        /// <summary>
        ///     Nesting that fits still reads, as it did.
        /// </summary>
        [TestMethod]
        public void NestingThatFitsReads()
        {
            var bytes = Serializer.ToByteArray(Nested(1000));
            var value = Deserializer.FromByteArray<object>(bytes);

            var depth = 0;
            for (var level = value as object[]; level != null && level.Length > 0; level = level[0] as object[])
            {
                ++depth;
            }

            Assert.AreEqual(1000, depth);
        }

        /// <summary>
        ///     A value nested too deeply to write throws as well, rather than ending the process.
        /// </summary>
        [TestMethod]
        [Timeout(120000)]
        public void NestingTooDeepToWriteThrows()
        {
            var value = Nested(TooDeep);
            var e = Assert.ThrowsExactly<SerializationException>(() => Serializer.ToByteArray(value));
            StringAssert.Contains(e.Message, "nested too deeply");
        }

        /// <summary>
        ///     A count larger than the input holds is refused before the allocation it asks for.
        /// </summary>
        [TestMethod]
        public void ACountTheInputCannotHoldIsRefused()
        {
            var bytes = LongArrayStating(25000000);
#if NETCOREAPP
            var before = GC.GetAllocatedBytesForCurrentThread();
#endif
            var e = Assert.ThrowsExactly<SerializationException>(() => Deserializer.FromByteArray<long[]>(bytes));
#if NETCOREAPP
            Assert.IsTrue(GC.GetAllocatedBytesForCurrentThread() - before < 1024 * 1024, "the stated count was allocated");
#endif
            StringAssert.Contains(e.Message, "25000000 items");
        }

        /// <summary>
        ///     The same from a stream that can tell how much it has left.
        /// </summary>
        [TestMethod]
        public void ACountASeekableStreamCannotHoldIsRefused()
        {
            var bytes = LongArrayStating(25000000);
            using (var stream = new MemoryStream(bytes))
            {
                Assert.ThrowsExactly<SerializationException>(() => Deserializer.Deserialize<long[]>(stream));
            }
        }

        /// <summary>
        ///     A truthful count still reads, from a byte array and from a stream.
        /// </summary>
        [TestMethod]
        public void TruthfulCountsRead()
        {
            var values = new long[1000];
            values[999] = 42;
            var list = new List<string> { "a", "b" };
            var bytes = Serializer.ToByteArray(values);

            Assert.AreEqual(42, Deserializer.FromByteArray<long[]>(bytes)[999]);
            using (var stream = new MemoryStream(bytes))
            {
                Assert.AreEqual(42, Deserializer.Deserialize<long[]>(stream)[999]);
            }

            using (var stream = new MemoryStream(Serializer.ToByteArray(list)))
            {
                CollectionAssert.AreEqual(list, Deserializer.Deserialize<List<string>>(stream));
            }
        }

        /// <summary>
        ///     A string longer than the stream holds is refused before its buffer is allocated.
        /// </summary>
        [TestMethod]
        public void AStringLongerThanTheStreamIsRefused()
        {
            var bytes = Serializer.ToByteArray("abc");
            var length = Array.IndexOf(bytes, (byte)3);
            Assert.IsTrue(length > 0 && bytes[length + 1] == (byte)'a', "the string's length is not where it was expected");

            var crafted = new List<byte>();
            crafted.AddRange(new ArraySegment<byte>(bytes, 0, length));
            crafted.AddRange(VarInt(1000000000));
            crafted.AddRange(new[] { (byte)'a', (byte)'b', (byte)'c' });

            using (var stream = new MemoryStream(crafted.ToArray()))
            {
                Assert.ThrowsExactly<EndOfStreamException>(() => Deserializer.Deserialize<string>(stream));
            }
        }

        /// <summary>
        ///     long[1] as the serializer writes it, with its count changed and one element left.
        /// </summary>
        private static byte[] LongArrayStating(int count)
        {
            var one = Serializer.ToByteArray(new long[1]);
            var two = Serializer.ToByteArray(new long[2]);

            // the count is the one byte where the two differ
            var at = 0;
            while (one[at] == two[at])
            {
                ++at;
            }

            Assert.AreEqual(1, one[at], "the count is not where it was expected");
            var crafted = new List<byte>();
            crafted.AddRange(new ArraySegment<byte>(one, 0, at));
            crafted.AddRange(VarInt(count));
            crafted.AddRange(new ArraySegment<byte>(one, at + 1, one.Length - at - 1));
            return crafted.ToArray();
        }

        private static IEnumerable<byte> VarInt(int value)
        {
            var v = (uint)value;
            while (v >= 0x80)
            {
                yield return (byte)(v | 0x80);
                v >>= 7;
            }

            yield return (byte)v;
        }

        private static object Nested(int depth)
        {
            object value = new object[0];
            for (var i = 0; i < depth; ++i)
            {
                value = new object[] { value };
            }

            return value;
        }

        /// <summary>
        ///     Writes a nesting on a thread whose stack holds it, as a crafted input would arrive.
        /// </summary>
        private static byte[] WrittenOnALargeStack(int depth)
        {
            byte[] bytes = null;
            Exception failure = null;
            var writer = new Thread(
                () =>
                    {
                        try
                        {
                            bytes = Serializer.ToByteArray(Nested(depth));
                        }
                        catch (Exception e)
                        {
                            failure = e;
                        }
                    },
                1024 * 1024 * 1024);
            writer.Start();
            writer.Join();
            if (failure != null)
            {
                throw new AssertFailedException("writing the nesting failed", failure);
            }

            return bytes;
        }
    }
}
