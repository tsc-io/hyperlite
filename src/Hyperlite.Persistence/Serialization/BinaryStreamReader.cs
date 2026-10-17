namespace Hyperlite.Persistence.Serialization
{
    using System.IO;
    using System.Text;

    /// <summary>
    ///     The reader for a stream: refuses a string longer than the stream has left.
    /// </summary>
    /// <remarks>
    ///     The framework's <c>BinaryReader.ReadString</c> on .NET Framework sizes its buffer
    ///     from the length the stream states, before reading a byte of the string, so a few
    ///     bytes could ask for gigabytes. A seekable stream knows what it has left, and a
    ///     longer string is refused before anything is allocated. A stream that cannot seek is
    ///     left to the framework, which on .NET reads in chunks of its own.
    /// </remarks>
    internal sealed class BinaryStreamReader : BinaryReader
    {
        private readonly Encoding encoding;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BinaryStreamReader" /> class,
        ///     leaving the stream open: it belongs to the caller.
        /// </summary>
        internal BinaryStreamReader(Stream stream, Encoding encoding)
            : base(stream, encoding, true)
        {
            this.encoding = encoding;
        }

        public override string ReadString()
        {
            var stream = this.BaseStream;
            if (!stream.CanSeek)
            {
                return base.ReadString();
            }

            var length = this.Read7BitEncodedInt();
            if (length < 0)
            {
                throw new IOException("Invalid string length " + length);
            }

            if (length == 0)
            {
                return string.Empty;
            }

            if (length > stream.Length - stream.Position)
            {
                throw new EndOfStreamException();
            }

            var bytes = this.ReadBytes(length);
            if (bytes.Length < length)
            {
                throw new EndOfStreamException();
            }

            return this.encoding.GetString(bytes);
        }
    }
}
