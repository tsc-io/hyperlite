namespace Hyperlite.Interop
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// One entry of a decoded namespace listing.
    /// </summary>
    internal sealed class ListingEntry
    {
        internal string Name;

        internal bool IsNamespace;

        internal List<ListingEntry> SubEntries;
    }

    /// <summary>
    /// Decodes the flat listing stream Hyperlite.Native produces.
    /// </summary>
    /// <remarks>
    /// The encoding, written by write_listing in api.cpp, is little endian:
    ///   list  := int32 count, entry * count
    ///   entry := int32 nameLength, byte * nameLength, byte isNamespace, list subEntries
    ///
    /// A flat stream rather than a call per entry, because a deep listing of a large
    /// namespace would otherwise cost thousands of boundary crossings.
    /// </remarks>
    internal static class ListingDecoder
    {
        internal static List<ListingEntry> Decode(byte[] encoded)
        {
            if (encoded == null || encoded.Length == 0)
            {
                return new List<ListingEntry>();
            }

            var at = 0;
            var result = ReadList(encoded, ref at);

            if (at != encoded.Length)
            {
                throw new InvalidOperationException(
                    "Malformed namespace listing: " + (encoded.Length - at) + " trailing bytes");
            }

            return result;
        }

        private static List<ListingEntry> ReadList(byte[] encoded, ref int at)
        {
            var count = ReadInt32(encoded, ref at);
            if (count < 0)
            {
                throw new InvalidOperationException("Malformed namespace listing: negative count");
            }

            var list = new List<ListingEntry>(count);
            for (var i = 0; i < count; ++i)
            {
                var entry = new ListingEntry();

                var nameLength = ReadInt32(encoded, ref at);
                if (nameLength < 0 || at + nameLength > encoded.Length)
                {
                    throw new InvalidOperationException("Malformed namespace listing: bad name length");
                }

                entry.Name = Encoding.UTF8.GetString(encoded, at, nameLength);
                at += nameLength;

                if (at >= encoded.Length)
                {
                    throw new InvalidOperationException("Malformed namespace listing: truncated entry");
                }

                entry.IsNamespace = encoded[at++] != 0;
                entry.SubEntries = ReadList(encoded, ref at);

                list.Add(entry);
            }

            return list;
        }

        private static int ReadInt32(byte[] encoded, ref int at)
        {
            if (at + 4 > encoded.Length)
            {
                throw new InvalidOperationException("Malformed namespace listing: truncated length");
            }

            var value = encoded[at]
                        | (encoded[at + 1] << 8)
                        | (encoded[at + 2] << 16)
                        | (encoded[at + 3] << 24);
            at += 4;
            return value;
        }
    }
}
