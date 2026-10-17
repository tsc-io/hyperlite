namespace Hyperlite.Interop
{
    using System;
    using System.Security.Cryptography;

    /// <summary>
    /// base85 encoded guids, the format Hypertable generated row keys use.
    /// </summary>
    /// <remarks>
    /// Bit for bit the ht4c::Common::KeyBuilder encoding, and the same as
    /// Hyperlite::key_builder in the native half: these keys end up in the database and
    /// have to decode back to the same guid whichever side produced them.
    ///
    /// It lives in managed code as well as native because <see cref="Key.Generate()" />,
    /// <see cref="Key.Encode" /> and <see cref="Key.Decode" /> are pure functions and
    /// need no boundary crossing. The native copy is only used where the mutator
    /// generates a key itself.
    /// </remarks>
    internal static class Base85
    {
        /// <summary>
        /// Bytes in a guid.
        /// </summary>
        internal const int GuidSize = 16;

        /// <summary>
        /// Characters in an encoded guid.
        /// </summary>
        internal const int KeySize = 20;

        /// <summary>
        /// Sentinel for a character that is not part of the alphabet.
        /// </summary>
        private const uint Invalid = 99;

        /// <summary>
        /// The alphabet: printable ascii from '!' up, skipping the five characters
        /// the postscript / cookie safe variant excludes.
        /// </summary>
        /// <summary>
        /// Random bytes are drawn 64 keys at a time, the same batch ht4c used.
        /// </summary>
        private const int PoolSize = 64 * GuidSize;

        private static readonly char[] ToChar = BuildToChar();

        private static readonly uint[] ToInt = BuildToInt();

#if NETFRAMEWORK || NETSTANDARD2_0
        /// <summary>
        /// One RNG for the process. RandomNumberGenerator.Create allocates a
        /// CryptoServiceProvider, which is far too expensive to do per key, and GetBytes
        /// is documented as thread safe.
        /// </summary>
        private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();
#endif

        [ThreadStatic]
        private static byte[] pool;

        [ThreadStatic]
        private static int poolOffset;

        /// <summary>
        /// Generates a fresh random key.
        /// </summary>
        internal static string Generate()
        {
            var guid = new byte[GuidSize];
            Fill(guid);

            // the shape a random uuid has: version 4, variant 10, so a decoded key
            // still looks like a v4 guid
            guid[7] = (byte)((guid[7] & 0x0F) | 0x40);
            guid[8] = (byte)((guid[8] & 0x3F) | 0x80);

            return Encode(guid);
        }

        /// <summary>
        /// Generates a fresh random key whose first two characters are taken from the
        /// prefix, so keys of one kind sort together.
        /// </summary>
        /// <param name="prefix">Up to two characters. Ignored where empty.</param>
        internal static string Generate(string prefix)
        {
            var key = Generate();
            if (string.IsNullOrEmpty(prefix))
            {
                return key;
            }

            var buffer = key.ToCharArray();
            buffer[0] = prefix[0];
            if (prefix.Length > 1)
            {
                buffer[1] = prefix[1];
            }

            return new string(buffer);
        }

        /// <summary>
        /// Encodes 16 bytes.
        /// </summary>
        internal static string Encode(byte[] guid)
        {
            if (guid == null)
            {
                throw new ArgumentNullException(nameof(guid));
            }

            if (guid.Length != GuidSize)
            {
                throw new ArgumentException("Invalid guid length", nameof(guid));
            }

            var buffer = new char[KeySize];
            var at = 0;

            for (var block = 0; block < GuidSize / 4; ++block)
            {
                // big endian, matching the htonl the original used
                var value = ((uint)guid[block * 4] << 24)
                            | ((uint)guid[(block * 4) + 1] << 16)
                            | ((uint)guid[(block * 4) + 2] << 8)
                            | guid[(block * 4) + 3];

                buffer[at + 4] = ToChar[value % 85];
                value /= 85;
                buffer[at + 3] = ToChar[value % 85];
                value /= 85;
                buffer[at + 2] = ToChar[value % 85];
                value /= 85;
                buffer[at + 1] = ToChar[value % 85];
                value /= 85;
                buffer[at] = ToChar[value];
                at += 5;
            }

            return new string(buffer);
        }

        /// <summary>
        /// Decodes a 20 character key back to 16 bytes.
        /// </summary>
        internal static byte[] Decode(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (key.Length != KeySize)
            {
                throw new ArgumentException("Invalid value length", "value");
            }

            var guid = new byte[GuidSize];
            var at = 0;

            for (var block = 0; block < GuidSize / 4; ++block)
            {
                uint value = 0;
                for (var digit = 0; digit < 5; ++digit)
                {
                    var c = key[at++];
                    var mapped = c < ToInt.Length ? ToInt[c] : Invalid;

                    // an out of alphabet character contributes nothing, which is what
                    // the original did as well rather than failing the whole decode
                    value = (value * 85) + (mapped == Invalid ? 0 : mapped);
                }

                guid[block * 4] = (byte)(value >> 24);
                guid[(block * 4) + 1] = (byte)(value >> 16);
                guid[(block * 4) + 2] = (byte)(value >> 8);
                guid[(block * 4) + 3] = (byte)value;
            }

            return guid;
        }

        /// <summary>
        /// Draws 16 random bytes from a per-thread pool.
        /// </summary>
        /// <remarks>
        /// The system RNG is asked once per refill rather than once per key, which is what
        /// ht4c::Common::KeyBuilder did and what makes key generation cheap enough to call
        /// per row. Going to the RNG per key costs two orders of magnitude more.
        ///
        /// Per-thread, so no lock is needed and two threads never hand out the same bytes.
        /// </remarks>
        private static void Fill(byte[] buffer)
        {
            if (pool == null)
            {
                pool = new byte[PoolSize];
                poolOffset = PoolSize;
            }

            if (poolOffset + GuidSize > PoolSize)
            {
                Refill(pool);
                poolOffset = 0;
            }

            Buffer.BlockCopy(pool, poolOffset, buffer, 0, GuidSize);
            poolOffset += GuidSize;
        }

        private static void Refill(byte[] buffer)
        {
#if NETFRAMEWORK || NETSTANDARD2_0
            Rng.GetBytes(buffer);
#else
            RandomNumberGenerator.Fill(buffer);
#endif
        }

        private static char[] BuildToChar()
        {
            var table = new char[85];
            var j = 0;
            for (var i = '!'; j < 85 && i < 127; ++i)
            {
                if (IsExcluded(i))
                {
                    continue;
                }

                table[j++] = i;
            }

            return table;
        }

        private static uint[] BuildToInt()
        {
            var table = new uint[256];
            for (var i = 0; i < table.Length; ++i)
            {
                table[i] = Invalid;
            }

            var j = 0u;
            for (var i = '!'; j < 85 && i < 127; ++i)
            {
                if (IsExcluded(i))
                {
                    continue;
                }

                table[i] = j++;
            }

            return table;
        }

        /// <summary>
        /// The five characters traditional postscript and cookie safe base85 leave out.
        /// </summary>
        private static bool IsExcluded(char c)
        {
            return c == ';' || c == '&' || c == '\\' || c == '"' || c == ',';
        }
    }
}
