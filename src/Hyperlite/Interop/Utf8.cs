namespace Hyperlite.Interop
{
    using System;
    using System.Text;

    /// <summary>
    /// UTF-8 conversions across the native boundary.
    /// </summary>
    /// <remarks>
    /// Every string in the ABI is UTF-8. Row keys, column families and qualifiers
    /// originate as .NET strings, so they round trip exactly. Cell values are
    /// arbitrary bytes and are only decoded when a regex has to see them, see
    /// <see cref="ToStringLossless" />.
    /// </remarks>
    internal static unsafe class Utf8
    {
        /// <summary>
        /// Strict UTF-8: an invalid sequence throws instead of collapsing to U+FFFD,
        /// which lets <see cref="ToStringLossless" /> detect non textual input.
        /// </summary>
        private static readonly Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        /// <summary>
        /// Byte per character, the fallback for values that are not valid UTF-8. Keeps
        /// distinct bytes distinct so a byte oriented pattern still behaves the way it
        /// did against RE2.
        /// </summary>
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        /// <summary>
        /// Length in bytes of the UTF-8 form of <paramref name="value" />.
        /// </summary>
        internal static int ByteCount(string value)
        {
            return string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
        }

        /// <summary>
        /// Encodes into a caller provided buffer, which must hold at least
        /// <see cref="ByteCount" /> bytes plus one for the terminator.
        /// </summary>
        /// <returns>Number of bytes written, excluding the terminator.</returns>
        internal static int Encode(string value, byte* destination, int capacity)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (capacity > 0)
                {
                    destination[0] = 0;
                }

                return 0;
            }

            int written;
            fixed (char* source = value)
            {
                written = Encoding.UTF8.GetBytes(source, value.Length, destination, capacity - 1);
            }

            destination[written] = 0;
            return written;
        }

        /// <summary>
        /// Encodes to a nul terminated byte array. Used where the value has to outlive
        /// the call, or is too large for a stack buffer.
        /// </summary>
        internal static byte[] Encode(string value)
        {
            if (value == null)
            {
                return null;
            }

            var count = Encoding.UTF8.GetByteCount(value);
            var bytes = new byte[count + 1];
            if (count > 0)
            {
                Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
            }

            return bytes;
        }

        /// <summary>
        /// Decodes a nul terminated UTF-8 string. Returns null for a null pointer.
        /// </summary>
        internal static string ToString(byte* utf8)
        {
            if (utf8 == null)
            {
                return null;
            }

            var length = 0;
            while (utf8[length] != 0)
            {
                ++length;
            }

            return ToString(utf8, length);
        }

        /// <summary>
        /// Decodes a counted UTF-8 string. Returns null for a null pointer, and empty
        /// for a zero length one - the same distinction the ABI makes.
        /// </summary>
        internal static string ToString(byte* utf8, int length)
        {
            if (utf8 == null)
            {
                return null;
            }

            if (length <= 0)
            {
                return string.Empty;
            }

#if NETFRAMEWORK
            // Ascii is the overwhelmingly common case - every row key, column family and
            // qualifier Hyperlite itself produces is ascii - and on .NET Framework widening
            // byte to char by hand beats the framework decoder by a wide margin: measured
            // over row-key sized input, 1.7x to 3.6x, which is about 24 ns per scanned
            // cell. This decode runs once per row and once per qualifier of every cell a
            // scan returns, so that is worth a hand written loop.
            //
            // Deliberately not compiled for .NET Core, where GetString is vectorised and
            // wins from about 32 bytes up.
            //
            // The string is mutated after construction, which is only safe because it was
            // just allocated here and has not been published anywhere. On the first byte
            // that is not ascii the half filled string is abandoned and the framework
            // decoder produces the answer, so multi-byte input stays exactly correct.
            var decoded = new string('\0', length);
            fixed (char* destination = decoded)
            {
                for (var i = 0; i < length; ++i)
                {
                    var b = utf8[i];
                    if (b >= 0x80)
                    {
                        return Encoding.UTF8.GetString(utf8, length);
                    }

                    destination[i] = (char)b;
                }
            }

            return decoded;
#else
            return Encoding.UTF8.GetString(utf8, length);
#endif
        }

        /// <summary>
        /// Slots in the small-string cache. A power of two, so the index is a mask.
        /// </summary>
        private const int CacheSlots = 8;

        /// <summary>
        /// Longest string the cache will hold. Above this a decode is cheap relative to
        /// the compare, and the value is unlikely to repeat anyway.
        /// </summary>
        private const int CacheMaxLength = 32;

        /// <summary>
        /// Per thread, so no lock is needed and two threads never fight over a slot.
        /// </summary>
        [ThreadStatic]
        private static string[] cache;

        /// <summary>
        /// Decodes a counted UTF-8 string, reusing the previous instance where the bytes
        /// repeat.
        /// </summary>
        /// <remarks>
        /// For the column family of a scanned cell this replaces one string allocation and
        /// one UTF-8 decode per cell with a length check and a byte compare: a table has a
        /// handful of families, so after the first cell of each the cache always hits - for an
        /// ascii name; any other is decoded every time, see below.
        ///
        /// Only for values from a small set. A row key is different on almost every cell,
        /// so caching one would miss every time and cost the lookup for nothing.
        ///
        /// The bytes are compared, not the pointer. Comparing pointers would be faster -
        /// the native side hands out the address of a name its schema owns, which is stable
        /// while the table is open - but this cache outlives any one table, and an address
        /// freed with the schema can be handed back for a different name of the same
        /// length. That would return the wrong family name, silently.
        /// </remarks>
        internal static string ToStringInterned(byte* utf8, int length)
        {
            if (utf8 == null)
            {
                return null;
            }

            if (length <= 0)
            {
                return string.Empty;
            }

            if (length > CacheMaxLength)
            {
                return ToString(utf8, length);
            }

            var slots = cache;
            if (slots == null)
            {
                slots = new string[CacheSlots];
                cache = slots;
            }

            var slot = ((utf8[0] * 31) + length) & (CacheSlots - 1);
            var hit = slots[slot];

            // Length here is a char count against a byte count, and Matches compares char
            // against byte: the two agree for ascii only. So only ascii bytes may hit - a
            // cached name of chars from U+0080 to U+00FF would otherwise equal, char for
            // byte, the UTF-8 of a different name, "Ã©" that of "é". Anything else misses
            // and takes the decode.
            if (hit != null && hit.Length == length && Matches(hit, utf8, length))
            {
                return hit;
            }

            var decoded = ToString(utf8, length);
            slots[slot] = decoded;
            return decoded;
        }

        private static bool Matches(string candidate, byte* utf8, int length)
        {
            for (var i = 0; i < length; ++i)
            {
                var b = utf8[i];
                if (b >= 0x80 || candidate[i] != (char)b)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Decodes for regex matching without losing information.
        /// </summary>
        /// <remarks>
        /// ht4c matched with RE2, which works on raw bytes. .NET Regex works on UTF-16,
        /// so the bytes have to be decoded first. Valid UTF-8 - which every row key and
        /// qualifier is, and which values normally are - decodes exactly. For anything
        /// else, plain UTF-8 decoding would map every invalid byte to U+FFFD and make
        /// distinct values compare equal, so those fall back to byte-per-character.
        /// </remarks>
        internal static string ToStringLossless(byte* bytes, int length)
        {
            if (bytes == null || length <= 0)
            {
                return string.Empty;
            }

            // Fast path for pure ascii, which every row key and qualifier in practice is
            // and most values are. It needs no validation and no multi-byte decoding, and
            // this runs once per scanned cell when a regex is in play, so it is worth the
            // extra pass to find out.
            var ascii = true;
            for (var i = 0; i < length; ++i)
            {
                if (bytes[i] >= 0x80)
                {
                    ascii = false;
                    break;
                }
            }

            if (ascii)
            {
                var chars = new string('\0', length);
                fixed (char* destination = chars)
                {
                    for (var i = 0; i < length; ++i)
                    {
                        destination[i] = (char)bytes[i];
                    }
                }

                return chars;
            }

            try
            {
                return StrictUtf8.GetString(bytes, length);
            }
            catch (DecoderFallbackException)
            {
                return Latin1.GetString(bytes, length);
            }
        }
    }
}
