namespace Hyperlite.Persistence.Serialization
{
    using System;
    using System.IO;

    internal sealed class FakeBinaryWriter : BinaryWriter
    {
        public FakeBinaryWriter()
            : base(new MemoryStream())
        {
        }

        public override Stream BaseStream { get; } = null;

        public override void Close() { }

        public override void Flush() { }

        public override long Seek(int offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void Write(char[] chars) { }

        public override void Write(long value) { }

        public override void Write(uint value) { }

        public override void Write(int value) { }

        public override void Write(ushort value) { }

        public override void Write(short value) { }

        public override void Write(decimal value) { }

        public override void Write(double value) { }

        public override void Write(float value) { }

        public override void Write(char ch) { }

        public override void Write(string value) { }

        public override void Write(byte[] buffer) { }

        public override void Write(sbyte value) { }

        public override void Write(byte value) { }

        public override void Write(bool value) { }

        public override void Write(ulong value) { }

        public override void Write(char[] chars, int index, int count) { }

        public override void Write(byte[] buffer, int index, int count) { }

        protected override void Dispose(bool disposing) { }
    }
}
