namespace Hyperlite.Interop
{
    using System;

    /// <summary>
    /// A native scan spec, built up call by call and handed to a scanner.
    /// </summary>
    /// <remarks>
    /// Short lived: created just before a scanner and released right after, since the
    /// scanner takes its own copy. Every string is encoded to UTF-8 on a stack buffer
    /// where it fits, so building a specification allocates nothing on the managed heap.
    /// </remarks>
    internal sealed unsafe class ScanSpecHandle : IDisposable
    {
        /// <summary>
        /// Strings longer than this get a heap buffer. Row keys, column names and
        /// patterns are far shorter in practice.
        /// </summary>
        private const int StackLimit = 512;

        private IntPtr handle;

        internal ScanSpecHandle()
        {
            IntPtr created;
            Native.Check(Native.Table.SsCreate(&created));
            this.handle = created;
        }

        /// <summary>
        /// The native handle, for passing to a scanner.
        /// </summary>
        internal IntPtr Handle
        {
            get
            {
                if (this.handle == IntPtr.Zero)
                {
                    throw new ObjectDisposedException(nameof(ScanSpecHandle));
                }

                return this.handle;
            }
        }

        public void Dispose()
        {
            if (this.handle != IntPtr.Zero)
            {
                var released = this.handle;
                this.handle = IntPtr.Zero;
                Native.Table.SsRelease(released);
            }
        }

        internal void SetInt(int field, long value)
        {
            Native.Check(Native.Table.SsSetI(this.Handle, field, value));
        }

        internal void SetString(int field, string value)
        {
            var count = Utf8.ByteCount(value);
            if (count < StackLimit)
            {
                var buffer = stackalloc byte[StackLimit];
                var written = Utf8.Encode(value, buffer, StackLimit);
                Native.Check(Native.Table.SsSetS(this.Handle, field, buffer, written));
                return;
            }

            var bytes = Utf8.Encode(value);
            fixed (byte* pinned = bytes)
            {
                Native.Check(Native.Table.SsSetS(this.Handle, field, pinned, bytes.Length - 1));
            }
        }

        internal void AddRow(string row)
        {
            var bytes = Utf8.Encode(row);
            fixed (byte* pinned = bytes)
            {
                Native.Check(Native.Table.SsAddRow(this.Handle, pinned, bytes != null ? bytes.Length - 1 : 0));
            }
        }

        internal void AddColumn(string column)
        {
            var bytes = Utf8.Encode(column);
            fixed (byte* pinned = bytes)
            {
                Native.Check(Native.Table.SsAddColumn(this.Handle, pinned, bytes != null ? bytes.Length - 1 : 0));
            }
        }

        internal void AddCell(string row, string column)
        {
            var rowBytes = Utf8.Encode(row);
            var columnBytes = Utf8.Encode(column);
            fixed (byte* pinnedRow = rowBytes)
            fixed (byte* pinnedColumn = columnBytes)
            {
                Native.Check(Native.Table.SsAddCell(
                    this.Handle,
                    pinnedRow,
                    rowBytes != null ? rowBytes.Length - 1 : 0,
                    pinnedColumn,
                    columnBytes != null ? columnBytes.Length - 1 : 0));
            }
        }

        internal void AddRowInterval(string from, bool includeFrom, string to, bool includeTo)
        {
            var fromBytes = Utf8.Encode(from);
            var toBytes = Utf8.Encode(to);
            fixed (byte* pinnedFrom = fromBytes)
            fixed (byte* pinnedTo = toBytes)
            {
                Native.Check(Native.Table.SsAddRowInterval(
                    this.Handle,
                    pinnedFrom,
                    fromBytes != null ? fromBytes.Length - 1 : 0,
                    includeFrom ? 1 : 0,
                    pinnedTo,
                    toBytes != null ? toBytes.Length - 1 : 0,
                    includeTo ? 1 : 0));
            }
        }

        internal void AddCellInterval(
            string fromRow,
            string fromColumn,
            bool includeFrom,
            string toRow,
            string toColumn,
            bool includeTo)
        {
            var fromRowBytes = Utf8.Encode(fromRow);
            var fromColumnBytes = Utf8.Encode(fromColumn);
            var toRowBytes = Utf8.Encode(toRow);
            var toColumnBytes = Utf8.Encode(toColumn);

            fixed (byte* pinnedFromRow = fromRowBytes)
            fixed (byte* pinnedFromColumn = fromColumnBytes)
            fixed (byte* pinnedToRow = toRowBytes)
            fixed (byte* pinnedToColumn = toColumnBytes)
            {
                Native.Check(Native.Table.SsAddCellInterval(
                    this.Handle,
                    pinnedFromRow,
                    fromRowBytes != null ? fromRowBytes.Length - 1 : 0,
                    pinnedFromColumn,
                    fromColumnBytes != null ? fromColumnBytes.Length - 1 : 0,
                    includeFrom ? 1 : 0,
                    pinnedToRow,
                    toRowBytes != null ? toRowBytes.Length - 1 : 0,
                    pinnedToColumn,
                    toColumnBytes != null ? toColumnBytes.Length - 1 : 0,
                    includeTo ? 1 : 0));
            }
        }

        internal void AddColumnPredicate(string columnFamily, string columnQualifier, int match, byte[] searchValue)
        {
            var familyBytes = Utf8.Encode(columnFamily);
            var qualifierBytes = Utf8.Encode(columnQualifier);

            fixed (byte* pinnedFamily = familyBytes)
            fixed (byte* pinnedQualifier = qualifierBytes)
            fixed (byte* pinnedValue = searchValue)
            {
                // A null search value stays null, an empty one is passed as a zero length
                // buffer: hypertable cannot tell the two apart in a predicate, but the
                // native side needs the distinction to decide whether the predicate has a
                // value at all.
                Native.Check(Native.Table.SsAddColumnPredicate(
                    this.Handle,
                    pinnedFamily,
                    familyBytes != null ? familyBytes.Length - 1 : 0,
                    pinnedQualifier,
                    qualifierBytes != null ? qualifierBytes.Length - 1 : 0,
                    match,
                    searchValue != null && searchValue.Length == 0 ? (byte*)EmptyMarker() : pinnedValue,
                    searchValue != null ? searchValue.Length : 0));
            }
        }

        /// <summary>
        /// A non-null pointer to nothing, for an empty but present search value. Points at
        /// a static byte so it stays valid for the duration of the call.
        /// </summary>
        private static byte* EmptyMarker()
        {
            return (byte*)EmptyValue.Address;
        }

        /// <summary>
        /// One pinned zero byte, allocated once.
        /// </summary>
        private static class EmptyValue
        {
            internal static readonly IntPtr Address = Allocate();

            private static IntPtr Allocate()
            {
                var pointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(1);
                System.Runtime.InteropServices.Marshal.WriteByte(pointer, 0);
                return pointer;
            }
        }
    }
}
