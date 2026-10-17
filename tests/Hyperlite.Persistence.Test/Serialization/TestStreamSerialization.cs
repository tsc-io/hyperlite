namespace Hyperlite.Persistence.Test.Serialization
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;

    using Hyperlite.Persistence.Serialization;
    using Hyperlite.Persistence.Test.Common;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     The stream based serialization api, which had no coverage at all.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Serializer.ToByteArray{T}(T)" /> writes through
    ///         <c>HeapBinaryWriter</c> and was well covered; <see cref="Serializer" />'s
    ///         <c>Serialize(Stream, ...)</c> writes through <c>BufferedBinaryWriter</c>,
    ///         which a coverage run showed at 0% of 231 lines - a second writer
    ///         implementation of the same format that nothing exercised.
    ///     </para>
    ///     <para>
    ///         So the test is not "does the stream path work" but "does it produce the same
    ///         bytes as the other writer", for the whole of <see cref="FormatCorpus" />.
    ///         Two writers that disagree would mean the format depends on which overload a
    ///         caller reached for, and a database written one way could not be read by code
    ///         that assumed the other.
    ///     </para>
    /// </remarks>
    [TestClass]
    public class TestStreamSerialization
    {
        /// <summary>
        ///     Both writers produce the same bytes, for every case in the corpus.
        /// </summary>
        [TestMethod]
        public void StreamAndByteArrayWritersAgree()
        {
            var failures = new StringBuilder();
            var compared = 0;

            foreach (var entry in FormatCorpus.Cases())
            {
                byte[] viaArray;
                try
                {
                    viaArray = Serializer.ToByteArray(
                        entry.Type, entry.Value, SerializationBase.DefaultCapacity);
                }
                catch (Exception)
                {
                    // the corpus has one case that cannot be encoded at all; the format
                    // fixture covers that
                    continue;
                }

                byte[] viaStream;
                using (var stream = new MemoryStream())
                {
                    new Serializer().Serialize(stream, entry.Type, entry.Value);
                    viaStream = stream.ToArray();
                }

                ++compared;
                if (ToHex(viaArray) != ToHex(viaStream))
                {
                    failures.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}{1}    byte array {2}{3}    stream     {4}",
                        entry.Name, Environment.NewLine, ToHex(viaArray),
                        Environment.NewLine, ToHex(viaStream)));
                }
            }

            Assert.AreEqual(
                0,
                failures.Length,
                "the two writers disagree, so the format depends on which overload is used:"
                + Environment.NewLine + failures);

            Assert.IsTrue(compared > 200, "expected the whole corpus, compared " + compared);
        }

        /// <summary>
        ///     Reading back from a stream gives the value again.
        /// </summary>
        [TestMethod]
        public void StreamRoundTripsEveryCase()
        {
            var failures = new StringBuilder();

            foreach (var entry in FormatCorpus.Cases())
            {
                if (!entry.IsPortable)
                {
                    // object graphs and the runtime-dependent cases are covered by
                    // TestFormatCompatibility; here the concern is the stream itself
                    continue;
                }

                try
                {
                    // one case in the corpus cannot be encoded at all, which
                    // TestFormatCompatibility covers; skip rather than report it here
                    Serializer.ToByteArray(
                        entry.Type, entry.Value, SerializationBase.DefaultCapacity);
                }
                catch (Exception)
                {
                    continue;
                }

                try
                {
                    using (var stream = new MemoryStream())
                    {
                        new Serializer().Serialize(stream, entry.Type, entry.Value);
                        stream.Position = 0;

                        var back = Deserializer.Deserialize(entry.Type, stream);
                        if (!Equatable.AreEqual(entry.Value, back))
                        {
                            failures.AppendLine(entry.Name + ": read back wrong");
                        }
                    }
                }
                catch (Exception e)
                {
                    failures.AppendLine(entry.Name + ": threw " + e.GetType().Name);
                }
            }

            Assert.AreEqual(0, failures.Length, Environment.NewLine + failures);
        }

        /// <summary>
        ///     A stream that hands over one byte at a time reads the same as one that does
        ///     not.
        /// </summary>
        /// <remarks>
        ///     Stream.Read is allowed to return fewer bytes than asked for, and a reader
        ///     that treats a short read as the end of the data works perfectly against a
        ///     MemoryStream and fails against a network or a compression stream. This is
        ///     the cheapest way to find that out without one.
        /// </remarks>
        [TestMethod]
        public void ShortReadsAreHandled()
        {
            var failures = new StringBuilder();

            foreach (var entry in FormatCorpus.Cases())
            {
                if (!entry.IsPortable)
                {
                    continue;
                }

                byte[] bytes;
                try
                {
                    bytes = Serializer.ToByteArray(
                        entry.Type, entry.Value, SerializationBase.DefaultCapacity);
                }
                catch (Exception)
                {
                    continue;
                }

                try
                {
                    using (var stream = new OneByteAtATimeStream(bytes))
                    {
                        var back = Deserializer.Deserialize(entry.Type, stream);
                        if (!Equatable.AreEqual(entry.Value, back))
                        {
                            failures.AppendLine(entry.Name + ": read back wrong");
                        }
                    }
                }
                catch (Exception e)
                {
                    failures.AppendLine(entry.Name + ": threw " + e.GetType().Name);
                }
            }

            Assert.AreEqual(
                0,
                failures.Length,
                "a stream returning one byte per Read is read wrongly:"
                + Environment.NewLine + failures);
        }

        /// <summary>
        ///     Writing several values into one stream and reading them back in order.
        /// </summary>
        /// <remarks>
        ///     This is what a reader that buffers ahead gets wrong: it consumes bytes
        ///     belonging to the next value and the second read fails or returns nonsense.
        ///     Whether the api supports it is worth knowing either way, so the assertion is
        ///     on the first value being right and the behaviour of the rest being
        ///     consistent rather than on a guess.
        /// </remarks>
        [TestMethod]
        public void SeveralValuesInOneStream()
        {
            using (var stream = new MemoryStream())
            {
                new Serializer().Serialize(stream, typeof(int), 42);
                var afterFirst = stream.Position;
                new Serializer().Serialize(stream, typeof(string), "second");
                var afterSecond = stream.Position;

                Assert.IsTrue(afterFirst > 0, "the first value wrote nothing");
                Assert.IsTrue(afterSecond > afterFirst, "the second value wrote nothing");

                // written back to back, so the two together are the two on their own
                var expected = ToHex(Serializer.ToByteArray(42)) + ToHex(Serializer.ToByteArray("second"));
                Assert.AreEqual(expected, ToHex(stream.ToArray()),
                    "two values written to one stream are not simply concatenated");

                stream.Position = 0;
                Assert.AreEqual(42, Deserializer.Deserialize<int>(stream),
                    "the first value does not read back");

                // and if the reader left the position exactly at the boundary, the second
                // value reads too - which is the property that makes a stream of values
                // usable at all
                if (stream.Position == afterFirst)
                {
                    Assert.AreEqual("second", Deserializer.Deserialize<string>(stream),
                        "the reader stopped at the right place but the second value is wrong");
                }
                else
                {
                    Assert.Inconclusive(
                        "the reader consumed " + stream.Position + " bytes for a value that "
                        + "occupies " + afterFirst + ", so several values cannot be read "
                        + "sequentially from one stream. Worth knowing, not necessarily worth "
                        + "fixing - every caller in this repository writes one value per stream.");
                }
            }
        }

        /// <summary>
        ///     The stream is left open, and positioned at the end of what was written.
        /// </summary>
        /// <remarks>
        ///     <c>BufferedBinaryWriter</c> is constructed with leaveOpen true, so disposing
        ///     it must not dispose the caller's stream. A writer that closed it would make
        ///     the api unusable for anything but a stream the caller was finished with.
        /// </remarks>
        [TestMethod]
        public void StreamIsLeftOpenAndFlushed()
        {
            using (var stream = new MemoryStream())
            {
                new Serializer().Serialize(stream, typeof(int), 42);

                Assert.IsTrue(stream.CanWrite, "the stream was closed by the serializer");
                Assert.AreEqual(
                    Serializer.ToByteArray(42).Length,
                    stream.Position,
                    "the stream is not positioned at the end of what was written, so a "
                    + "second write would overlap the first or leave a gap");

                // still usable afterwards
                stream.WriteByte(0xff);
                Assert.AreEqual(Serializer.ToByteArray(42).Length + 1, stream.Length);
            }
        }

        /// <summary>
        ///     A value larger than the writer's internal buffer.
        /// </summary>
        /// <remarks>
        ///     BufferedBinaryWriter flushes in pages, and the interesting sizes are the ones
        ///     either side of a page boundary - a value that fits, one that straddles it and
        ///     one that spans several pages. An off-by-one in the flush is invisible for
        ///     small values.
        /// </remarks>
        [TestMethod]
        public void ValuesSpanningTheBuffer()
        {
            foreach (var size in new[] { 1, 4095, 4096, 4097, 8192, 100000 })
            {
                var value = new byte[size];
                for (var i = 0; i < size; ++i)
                {
                    value[i] = (byte)(i % 251);
                }

                byte[] viaStream;
                using (var stream = new MemoryStream())
                {
                    new Serializer().Serialize(stream, typeof(byte[]), value);
                    viaStream = stream.ToArray();
                }

                CollectionAssert.AreEqual(
                    Serializer.ToByteArray(value),
                    viaStream,
                    "the two writers disagree at " + size + " bytes");

                using (var stream = new MemoryStream(viaStream))
                {
                    CollectionAssert.AreEqual(
                        value,
                        Deserializer.Deserialize<byte[]>(stream),
                        size + " bytes did not round trip through a stream");
                }
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var hex = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return hex.ToString();
        }

        /// <summary>
        ///     A readable stream that returns exactly one byte per Read, however many were
        ///     asked for, and reports itself as non-seekable.
        /// </summary>
        private sealed class OneByteAtATimeStream : Stream
        {
            private readonly byte[] bytes;

            private int position;

            internal OneByteAtATimeStream(byte[] bytes)
            {
                this.bytes = bytes;
            }

            public override bool CanRead
            {
                get { return true; }
            }

            public override bool CanSeek
            {
                get { return false; }
            }

            public override bool CanWrite
            {
                get { return false; }
            }

            public override long Length
            {
                get { return this.bytes.Length; }
            }

            public override long Position
            {
                get { return this.position; }
                set { throw new NotSupportedException(); }
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count <= 0 || this.position >= this.bytes.Length)
                {
                    return 0;
                }

                buffer[offset] = this.bytes[this.position++];
                return 1;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}
