namespace Hyperlite.Persistence.Test.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;

    using Hyperlite.Persistence.Serialization;
    using Hyperlite.Persistence.Test.Common;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Pins the serialized form of <see cref="FormatCorpus" />, byte for byte.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This exists because the round-trip fixtures beside it cannot catch a format
    ///         change. Those tests hand a value to the serializer and read it back with the
    ///         deserializer of the same build, which proves the writer and the reader agree
    ///         with each other - and would pass just as happily if both changed together
    ///         and stopped agreeing with every byte already written to a database.
    ///     </para>
    ///     <para>
    ///         The expected bytes are in <c>format-golden.txt</c>, embedded beside this
    ///         file. Keeping them in a data file rather than in an array here is
    ///         deliberate: a change to the format then shows up as a diff of that file, one
    ///         line per case, which is reviewable in a way that a wall of hex literals is
    ///         not. Regenerate with <c>Hyperlite.Bench.exe --format-golden</c>, and only
    ///         when changing the format is the intention.
    ///     </para>
    ///     <para>
    ///         Each portable case is checked in both directions, and the second is the one
    ///         that states the contract:
    ///     </para>
    ///     <list type="number">
    ///         <item>
    ///             serializing the value produces exactly these bytes - the writer has not
    ///             moved
    ///         </item>
    ///         <item>
    ///             deserializing these bytes produces the value again - the reader still
    ///             reads what an older version wrote
    ///         </item>
    ///     </list>
    ///     <para>
    ///         Cases named <c>.np</c> are not portable: their bytes embed an
    ///         assembly-qualified type name, so pinning them would fail on any rename and
    ///         say nothing about the format. They are covered by round trip, and by the
    ///         reference-identity assertions further down - the part of an object graph a
    ///         value comparison cannot see.
    ///     </para>
    ///     <para>
    ///         Every case is accounted for: a portable case with no line in the golden file
    ///         fails, and so does a line with no case. Without that a case could be added
    ///         to the corpus, skipped here in silence, and look covered.
    ///     </para>
    /// </remarks>
    [TestClass]
    public class TestFormatCompatibility
    {
        /// <summary>
        ///     How many truncated inputs are currently accepted without complaint. Cutting
        ///     the last byte of a varint can leave a shorter but still valid number, so
        ///     this cannot be zero; it is pinned so that a rise is noticed.
        /// </summary>
        private const int AcceptedTruncations = 90;

        /// <summary>
        ///     The embedded golden file, as name to hex.
        /// </summary>
        private static readonly Lazy<IDictionary<string, string>> Golden =
            new Lazy<IDictionary<string, string>>(ReadGolden);

        /// <summary>
        ///     Serializing each value produces exactly the bytes recorded for it.
        /// </summary>
        [TestMethod]
        public void SerializedFormIsUnchanged()
        {
            var failures = new StringBuilder();
            var compared = 0;

            foreach (var entry in FormatCorpus.Cases())
            {
                string expected;
                if (!entry.IsPortable || !Golden.Value.TryGetValue(entry.Name, out expected))
                {
                    continue;
                }

                string actual;
                try
                {
                    actual = ToHex(Serializer.ToByteArray(
                        entry.Type, entry.Value, SerializationBase.DefaultCapacity));
                }
                catch (Exception e)
                {
                    failures.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: threw {1}", entry.Name, e.GetType().Name));
                    continue;
                }

                ++compared;
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    failures.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}{1}    expected {2}{3}    actual   {4}",
                        entry.Name, Environment.NewLine, expected, Environment.NewLine, actual));
                }
            }

            Assert.AreEqual(
                0,
                failures.Length,
                "the serialized form changed, which breaks every database already written:"
                + Environment.NewLine + failures);

            Assert.IsTrue(
                compared > 200,
                "expected the corpus to be substantial, compared only " + compared);
        }

        /// <summary>
        ///     Deserializing the recorded bytes produces the value again - the direction
        ///     that says an older database can still be read.
        /// </summary>
        [TestMethod]
        public void RecordedFormStillDeserializes()
        {
            var failures = new StringBuilder();

            foreach (var entry in FormatCorpus.Cases())
            {
                string hex;
                if (!entry.IsPortable || !Golden.Value.TryGetValue(entry.Name, out hex))
                {
                    continue;
                }

                object actual;
                try
                {
                    actual = Deserializer.FromByteArray(entry.Type, FromHex(hex));
                }
                catch (Exception e)
                {
                    failures.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: threw {1}", entry.Name, e.GetType().Name));
                    continue;
                }

                if (!Equatable.AreEqual(entry.Value, actual))
                {
                    failures.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: expected {1}, got {2}",
                        entry.Name, Describe(entry.Value), Describe(actual)));
                }
            }

            Assert.AreEqual(
                0,
                failures.Length,
                "bytes an earlier version wrote no longer read back:"
                + Environment.NewLine + failures);
        }

        /// <summary>
        ///     Every case is either pinned or explicitly not portable, and every recorded
        ///     line belongs to a case.
        /// </summary>
        [TestMethod]
        public void CorpusAndGoldenFileAgree()
        {
            var cases = FormatCorpus.Cases();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new List<string>();

            foreach (var entry in cases)
            {
                if (!names.Add(entry.Name))
                {
                    duplicates.Add(entry.Name);
                }
            }

            Assert.AreEqual(
                0,
                duplicates.Count,
                "duplicate case names: " + string.Join(", ", duplicates.ToArray()));

            var missing = new List<string>();
            foreach (var entry in cases)
            {
                if (entry.IsPortable
                    && CanSerialize(entry)
                    && !Golden.Value.ContainsKey(entry.Name))
                {
                    missing.Add(entry.Name);
                }
            }

            var orphaned = new List<string>();
            foreach (var name in Golden.Value.Keys)
            {
                if (!names.Contains(name))
                {
                    orphaned.Add(name);
                }
            }

            Assert.AreEqual(
                0,
                missing.Count,
                "portable cases with no recorded bytes - regenerate format-golden.txt: "
                + string.Join(", ", missing.ToArray()));

            Assert.AreEqual(
                0,
                orphaned.Count,
                "recorded bytes with no case in the corpus: "
                + string.Join(", ", orphaned.ToArray()));
        }

        /// <summary>
        ///     A char that is half of a surrogate pair cannot be encoded, and says so.
        /// </summary>
        /// <remarks>
        ///     Pinned because the alternative - substituting U+FFFD - would corrupt
        ///     silently, and because it is the one case in the corpus that does not
        ///     serialize at all.
        /// </remarks>
        [TestMethod]
        public void LoneSurrogateIsRejected()
        {
            var threw = false;
            try
            {
                Serializer.ToByteArray('\ud83d');
            }
            catch (Exception)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "a lone high surrogate is not encodable and must not be accepted");

            Assert.AreEqual(
                "🚀",
                Deserializer.FromByteArray<string>(Serializer.ToByteArray("🚀")),
                "a complete surrogate pair is fine");
        }

        /// <summary>
        ///     The cases whose bytes cannot be pinned still round-trip, and do so
        ///     reproducibly.
        /// </summary>
        [TestMethod]
        public void ObjectGraphsRoundTrip()
        {
            var failures = new StringBuilder();

            foreach (var entry in FormatCorpus.Cases())
            {
                // everything the golden file does not pin: the assembly-dependent cases
                // and the runtime-dependent ones. There is no skip list any more - there was
                // one, holding the three collections that came back empty, and it is how the
                // loss stayed invisible
                if (entry.IsPortable)
                {
                    continue;
                }

                try
                {
                    var bytes = Serializer.ToByteArray(
                        entry.Type, entry.Value, SerializationBase.DefaultCapacity);
                    var back = Deserializer.FromByteArray(entry.Type, bytes);

                    if (entry.Value == null)
                    {
                        if (back != null)
                        {
                            failures.AppendLine(entry.Name + ": null came back non null");
                        }

                        continue;
                    }

                    if (back == null)
                    {
                        failures.AppendLine(entry.Name + ": came back null");
                        continue;
                    }

                    if (back.GetType() != entry.Value.GetType())
                    {
                        failures.AppendLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}: came back as {1}, expected {2}",
                            entry.Name, back.GetType().Name, entry.Value.GetType().Name));
                    }

                    // the bytes also have to be stable between two serializations of one
                    // value, which a dictionary iterated in hash order would not be
                    var again = Serializer.ToByteArray(
                        entry.Type, entry.Value, SerializationBase.DefaultCapacity);
                    if (!string.Equals(ToHex(bytes), ToHex(again), StringComparison.Ordinal))
                    {
                        failures.AppendLine(entry.Name + ": two serializations of one value differ");
                    }
                }
                catch (Exception e)
                {
                    failures.AppendLine(string.Format(
                        CultureInfo.InvariantCulture, "{0}: threw {1}", entry.Name, e));
                }
            }

            Assert.AreEqual(0, failures.Length, Environment.NewLine + failures);
        }

        /// <summary>
        ///     A shared reference stays shared and a cycle closes, which is what the
        ///     ObjectRef tag is for.
        /// </summary>
        [TestMethod]
        public void ReferenceIdentityIsPreserved()
        {
            var shared = new Leaf { Id = 7, Name = "leaf" };
            var pair = Deserializer.FromByteArray<Leaf[]>(
                Serializer.ToByteArray(new[] { shared, shared }));

            Assert.IsNotNull(pair);
            Assert.AreEqual(2, pair.Length);
            Assert.AreEqual(7, pair[0].Id);
            Assert.AreEqual("leaf", pair[0].Name);
            Assert.AreSame(pair[0], pair[1], "a shared object must come back shared, not copied");

            var root = new Node { Name = "root" };
            root.Child = new Node { Name = "child", Parent = root };

            var back = Deserializer.FromByteArray<Node>(Serializer.ToByteArray(root));
            Assert.IsNotNull(back);
            Assert.AreEqual("root", back.Name);
            Assert.IsNotNull(back.Child);
            Assert.AreEqual("child", back.Child.Name);
            Assert.AreSame(back, back.Child.Parent, "the cycle must close back onto the root");

            var self = new Node { Name = "self" };
            self.Parent = self;
            var backSelf = Deserializer.FromByteArray<Node>(Serializer.ToByteArray(self));
            Assert.AreSame(backSelf, backSelf.Parent, "a self reference must close onto itself");
        }

        /// <summary>
        ///     Deserializing truncated bytes fails rather than returning something wrong.
        /// </summary>
        /// <remarks>
        ///     A reader that runs off the end of a buffer and returns a plausible value is
        ///     worse than one that throws, because the damage is silent. Not every
        ///     truncation is detectable - cutting the last byte of a varint can leave a
        ///     shorter but valid number - so this pins how many survive rather than
        ///     demanding none. The number moving is the signal.
        /// </remarks>
        [TestMethod]
        public void TruncatedInputIsRejected()
        {
            var survived = new List<string>();

            foreach (var entry in FormatCorpus.Cases())
            {
                string hex;
                if (!entry.IsPortable || !Golden.Value.TryGetValue(entry.Name, out hex))
                {
                    continue;
                }

                var bytes = FromHex(hex);
                if (bytes.Length < 2)
                {
                    // a single tag byte carries the whole value, so there is nothing to cut
                    continue;
                }

                var truncated = new byte[bytes.Length - 1];
                Array.Copy(bytes, truncated, truncated.Length);

                try
                {
                    Deserializer.FromByteArray(entry.Type, truncated);
                    survived.Add(entry.Name);
                }
                catch (Exception)
                {
                    // expected
                }
            }

            Assert.IsTrue(
                survived.Count <= AcceptedTruncations,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} truncated inputs were accepted silently, up from {1}:{2}{3}",
                    survived.Count,
                    AcceptedTruncations,
                    Environment.NewLine,
                    string.Join(Environment.NewLine, survived.ToArray())));
        }

        /// <summary>
        ///     A Type written on one runtime reads back on the other.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Every value that writes a corelib type name writes a different one per
        ///         runtime: .NET Framework says mscorlib, .NET says System.Private.CoreLib.
        ///         So a database holding a Type - or a LinkedList, Queue, Stack or nested
        ///         Tuple, which write their concrete type - carries a name that does not
        ///         exist on the other runtime, and reading it depends on the binder
        ///         forwarding it.
        ///     </para>
        ///     <para>
        ///         Both forms are hard coded here, produced by this repository on each
        ///         runtime, and both are read on whichever runtime is running. That is the
        ///         migration case: a database written by a .NET Framework process and opened
        ///         by a .NET one, or the reverse.
        ///     </para>
        /// </remarks>
        [TestMethod]
        public void TypeWrittenOnEitherRuntimeReadsBack()
        {
            // typeof(int), as written by net48 and by net8.0 respectively
            const string FrameworkForm =
                "4a296d73636f726c69622c205075626c69634b6579546f6b656e3d62373761356335363139333465303839"
                + "0c53797374656d2e496e743332";
            const string CoreForm =
                "4a3753797374656d2e507269766174652e436f72654c69622c205075626c69634b6579546f6b656e3d3763"
                + "65633835643762656137373938650c53797374656d2e496e743332";

            // the form this runtime produces must be one of the two, or the corpus is stale
            var mine = ToHex(Serializer.ToByteArray(typeof(int)));
            Assert.IsTrue(
                mine == FrameworkForm || mine == CoreForm,
                "this runtime writes a third form for typeof(int), which the two constants "
                + "above no longer cover: " + mine);

            Assert.AreEqual(
                typeof(int),
                Deserializer.FromByteArray<Type>(FromHex(FrameworkForm)),
                "a Type written by .NET Framework must read back here");

            Assert.AreEqual(
                typeof(int),
                Deserializer.FromByteArray<Type>(FromHex(CoreForm)),
                "a Type written by .NET must read back here");
        }

        /// <summary>
        ///     A nullable value type reads back as that value type, not as something the
        ///     caller then cannot cast.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Added because expanding the corpus found this broken for nullable enums
        ///         and only for them. The bytes were always right - they are pinned in
        ///         format-golden.txt and did not change when it was fixed - but the reader
        ///         handed back the underlying integer: the non-generic overload returned an
        ///         Int32 where a TEnum was asked for, the generic overload threw
        ///         InvalidCastException turning that into a TEnum?, and an array of
        ///         nullable enum threw as its elements were stored.
        ///     </para>
        ///     <para>
        ///         The cause was ordering in Type.Convert: it tested IsEnum before
        ///         unwrapping the Nullable, and Nullable&lt;TEnum&gt; is not itself an enum.
        ///         int?, DateTime? and Guid? were unaffected, which is why a narrower
        ///         corpus missed it - a boxed int is a valid int?, so only the enum case
        ///         needed the conversion the enum branch does.
        ///     </para>
        /// </remarks>
        [TestMethod]
        public void NullableValueTypesReadBackAsThemselves()
        {
            Assert.AreEqual(
                Weekday.Wednesday,
                Deserializer.FromByteArray(typeof(Weekday?), Serializer.ToByteArray((Weekday?)Weekday.Wednesday)),
                "a nullable enum must come back as the enum, not as its underlying integer");

            Assert.AreEqual(
                Weekday.Wednesday,
                Deserializer.FromByteArray<Weekday?>(Serializer.ToByteArray((Weekday?)Weekday.Wednesday)),
                "and the generic overload must not throw casting it");

            Assert.IsNull(
                Deserializer.FromByteArray<Weekday?>(Serializer.ToByteArray((Weekday?)null)),
                "a null nullable enum stays null");

            var array = Deserializer.FromByteArray<Weekday?[]>(
                Serializer.ToByteArray(new Weekday?[] { Weekday.Wednesday, null }));
            Assert.IsNotNull(array);
            Assert.AreEqual(2, array.Length);
            Assert.AreEqual(Weekday.Wednesday, array[0]);
            Assert.IsNull(array[1]);

            // the underlying type of the enum must not change the answer
            Assert.AreEqual(
                Small.Two,
                Deserializer.FromByteArray<Small?>(Serializer.ToByteArray((Small?)Small.Two)));
            Assert.AreEqual(
                Big.Large,
                Deserializer.FromByteArray<Big?>(Serializer.ToByteArray((Big?)Big.Large)));

            // and the cases that already worked still do
            Assert.AreEqual(42, Deserializer.FromByteArray<int?>(Serializer.ToByteArray((int?)42)));
            Assert.AreEqual(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Deserializer.FromByteArray<DateTime?>(
                    Serializer.ToByteArray((DateTime?)new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))));
            Assert.AreEqual(
                Guid.Empty, Deserializer.FromByteArray<Guid?>(Serializer.ToByteArray((Guid?)Guid.Empty)));
            Assert.AreEqual(
                TimeSpan.Zero,
                Deserializer.FromByteArray<TimeSpan?>(Serializer.ToByteArray((TimeSpan?)TimeSpan.Zero)));
            Assert.AreEqual(
                true, Deserializer.FromByteArray<bool?>(Serializer.ToByteArray((bool?)true)));
        }

        /// <summary>
        ///     LinkedList, Queue and Stack keep their elements, and their order.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         These three used to come back empty, and the elements were never written:
        ///         a LinkedList&lt;int&gt; of two elements was 144 bytes, 137 of them the
        ///         assembly-qualified type name and none of them content. One flag caused it
        ///         on both sides - <c>Inspector.IsCollection</c> is
        ///         <c>InspectedEnumerable.HasAdd</c>, the serializer needs it to write the
        ///         collection form and writes an object without it, and the read paths need
        ///         it to fill one. It was false because the adder was looked up by the name
        ///         Add: Queue has Enqueue, Stack has Push, and LinkedList implements
        ///         <c>ICollection&lt;T&gt;.Add</c> explicitly.
        ///     </para>
        ///     <para>
        ///         Fixing it changed the bytes for these three types, from an empty object to
        ///         a collection. Nothing that round-tripped before changed - the golden file
        ///         is byte for byte what it was - but a reader without the fix now throws on
        ///         them where it used to return empty quietly. That is the trade: three types
        ///         that carried no data at all now carry it.
        ///     </para>
        ///     <para>
        ///         Accepted on the grounds that no stored data uses any of
        ///         the three - which is the only thing that made it free. Anyone weighing a
        ///         similar change should not read this as a precedent: what made it safe was
        ///         the absence of data, not the smallness of the change.
        ///     </para>
        ///     <para>
        ///         The order matters as much as the contents. A stack enumerates from its
        ///         top, so the form on the wire holds the last element pushed first, and a
        ///         reader that pushes them back in that order returns the stack inverted -
        ///         with the right elements, which is why the contents alone are not enough to
        ///         assert.
        ///     </para>
        /// </remarks>
        [TestMethod]
        public void CollectionsWithAnUnconventionalAdderKeepTheirOrder()
        {
            // enumerates in insertion order, so the wire order is the insertion order
            CollectionAssert.AreEqual(
                new[] { 1, 2, 3 },
                Deserializer.FromByteArray<LinkedList<int>>(
                    Serializer.ToByteArray(new LinkedList<int>(new[] { 1, 2, 3 }))).ToArray(),
                "a LinkedList came back with the wrong contents or in the wrong order");

            // first in, first out: likewise
            CollectionAssert.AreEqual(
                new[] { 1, 2, 3 },
                Deserializer.FromByteArray<Queue<int>>(
                    Serializer.ToByteArray(new Queue<int>(new[] { 1, 2, 3 }))).ToArray(),
                "a Queue came back with the wrong contents or in the wrong order");

            // last in, first out: the wire order is 3, 2, 1 and the reader pushes in reverse
            var stack = Deserializer.FromByteArray<Stack<int>>(
                Serializer.ToByteArray(new Stack<int>(new[] { 1, 2, 3 })));
            CollectionAssert.AreEqual(
                new[] { 3, 2, 1 },
                stack.ToArray(),
                "a Stack came back with the wrong contents or in the wrong order");
            Assert.AreEqual(3, stack.Peek(), "the top of the stack is not the last pushed");

            // a nested one, so the element path is exercised rather than only the primitive
            var nested = Deserializer.FromByteArray<Queue<string>>(
                Serializer.ToByteArray(new Queue<string>(new[] { "a", null, "c" })));
            CollectionAssert.AreEqual(new[] { "a", null, "c" }, nested.ToArray());

            // ReadOnlyCollection is why the adder is an allowlist and not a lookup of the
            // explicitly implemented ICollection<T>.Add: it has one too, and it throws. It
            // has to keep serializing the way it did.
            var readOnly = Deserializer.FromByteArray<ReadOnlyCollection<int>>(
                Serializer.ToByteArray(new ReadOnlyCollection<int>(new[] { 1, 2 })));
            Assert.IsNotNull(readOnly, "a ReadOnlyCollection no longer reads back at all");

            // and the ones that always worked, so a change here cannot break them unnoticed
            CollectionAssert.AreEqual(
                new[] { 1, 2, 3 },
                Deserializer.FromByteArray<List<int>>(Serializer.ToByteArray(new List<int> { 1, 2, 3 })));
            Assert.AreEqual(
                1,
                Deserializer.FromByteArray<HashSet<string>>(
                    Serializer.ToByteArray(new HashSet<string> { "x" })).Count);
        }

        private static bool CanSerialize(FormatCorpus.Case entry)
        {
            try
            {
                Serializer.ToByteArray(entry.Type, entry.Value, SerializationBase.DefaultCapacity);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IDictionary<string, string> ReadGolden()
        {
            var assembly = typeof(TestFormatCompatibility).GetTypeInfo().Assembly;
            var resource = Array.Find(
                assembly.GetManifestResourceNames(),
                n => n.EndsWith("format-golden.txt", StringComparison.Ordinal));

            Assert.IsNotNull(
                resource,
                "format-golden.txt is not embedded; available: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

            var golden = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var stream = assembly.GetManifestResourceStream(resource))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed[0] == '#')
                    {
                        continue;
                    }

                    var tab = trimmed.IndexOf('\t');
                    Assert.IsTrue(tab > 0, "malformed line in format-golden.txt: " + line);
                    golden.Add(trimmed.Substring(0, tab), trimmed.Substring(tab + 1).Trim());
                }
            }

            return golden;
        }

        private static string Describe(object value)
        {
            if (value == null)
            {
                return "null";
            }

            var array = value as Array;
            if (array != null)
            {
                return value.GetType().Name + " of " + array.Length;
            }

            return value.ToString();
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

        private static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; ++i)
            {
                bytes[i] = byte.Parse(
                    hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return bytes;
        }

        private enum Weekday
        {
            Wednesday = 3
        }

        private enum Small : byte
        {
            Two = 2
        }

        private enum Big : long
        {
            Large = 1234567890123L
        }

        private sealed class Leaf
        {
            public int Id { get; set; }

            public string Name { get; set; }
        }

        private sealed class Node
        {
            public string Name { get; set; }

            public Node Child { get; set; }

            public Node Parent { get; set; }
        }
    }
}
