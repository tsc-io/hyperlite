// Prints the serialized form of the pinned corpus, as hex.
//
// The cases live in FormatCorpus, which is compiled into this project and into both test
// projects - one definition, so the printer and the fixture cannot drift apart. What is
// here is only the printing.
//
// Two uses:
//
//   - diffing two builds of this repository. Run before a change to the serializer, run
//     after, compare. Identical output means the bytes on the wire did not move. Hex
//     rather than a hash so that a difference says *where*.
//
//         Hyperlite.Bench.exe --format-digest > before.txt
//         ... change something ...
//         Hyperlite.Bench.exe --format-digest > after.txt
//         fc before.txt after.txt
//
//   - producing the expected bytes for TestFormatCompatibility. --format-golden writes
//     them in the form that fixture reads, and only the portable cases, since the rest
//     embed an assembly-qualified type name.

namespace Hyperlite.Bench
{
    using System;
    using System.Globalization;
    using System.Text;

    using Hyperlite.Persistence.Serialization;
    using Hyperlite.Persistence.Test.Serialization;

    internal static class FormatDigest
    {
        /// <summary>
        ///     Prints every case as hex, for diffing one build against another.
        /// </summary>
        internal static void Run()
        {
            Console.WriteLine("# serialized form of the pinned corpus, as hex");
            Console.WriteLine("# a diff against an earlier run shows any change to the format");
            Console.WriteLine("# names ending .np embed an assembly qualified type name and are not portable");
            Console.WriteLine();

            var cases = FormatCorpus.Cases();
            foreach (var entry in cases)
            {
                string hex;
                if (!TryHex(entry, out hex))
                {
                    Console.WriteLine("FORMAT\t{0}\t{1}", entry.Name, hex);
                    continue;
                }

                Console.WriteLine("FORMAT\t{0}\t{1}\t{2}", entry.Name, hex.Length / 2, hex);
            }

            Console.WriteLine();
            Console.WriteLine("# {0} cases", cases.Count);
        }

        /// <summary>
        ///     Writes the expected bytes in the form TestFormatCompatibility reads.
        /// </summary>
        /// <remarks>
        ///     Only the portable cases: the rest embed an assembly-qualified type name, so
        ///     pinning their bytes would fail on any rename and say nothing about the
        ///     format. The fixture covers those by round trip.
        /// </remarks>
        internal static void Golden()
        {
            Console.WriteLine("# expected serialized form, one case per line: name<tab>hex");
            Console.WriteLine("# regenerate with Hyperlite.Bench.exe --format-golden");
            Console.WriteLine("#");
            Console.WriteLine("# A line changing here means the bytes on the wire changed, which breaks every");
            Console.WriteLine("# database already written. Regenerate only when that is the intention.");

            var written = 0;
            var group = string.Empty;

            foreach (var entry in FormatCorpus.Cases())
            {
                if (!entry.IsPortable)
                {
                    continue;
                }

                string hex;
                if (!TryHex(entry, out hex))
                {
                    // a case that cannot be serialized has no bytes to pin; the fixture
                    // asserts the throw instead
                    continue;
                }

                var prefix = entry.Name.Split('.')[0];
                if (prefix != group)
                {
                    Console.WriteLine();
                    group = prefix;
                }

                Console.WriteLine("{0}\t{1}", entry.Name, hex);
                ++written;
            }

            Console.WriteLine();
            Console.WriteLine("# {0} cases", written);
        }

        private static bool TryHex(FormatCorpus.Case entry, out string hex)
        {
            byte[] bytes;
            try
            {
                bytes = Serializer.ToByteArray(entry.Type, entry.Value, SerializationBase.DefaultCapacity);
            }
            catch (Exception e)
            {
                hex = "THREW " + e.GetType().Name;
                return false;
            }

            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            hex = sb.ToString();
            return true;
        }
    }
}
