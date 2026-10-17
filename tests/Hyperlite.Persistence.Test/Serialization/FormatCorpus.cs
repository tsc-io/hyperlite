// The corpus whose serialized form is pinned, defined once and used twice.
//
// TestFormatCompatibility asserts that every case below still serializes to exactly the
// bytes recorded for it, and that those bytes still deserialize back to the value.
// Hyperlite.Bench --format-digest prints the same cases as hex, which is how the recorded
// bytes are produced and how two builds are diffed against each other.
//
// One definition rather than two, because two would drift: a case added to the printer
// and not to the test would look covered and not be. This file is compiled into
// Hyperlite.Persistence.Test, Hyperlite.Serialization.Test and Hyperlite.Bench.
//
// What belongs here is anything the format can express. The cases are derived from Tags,
// ArrayFlags, CollectionFlags and DictionaryFlags rather than chosen by feel: every tag
// the encoder can write appears at least once, every tag with a dedicated short form -
// Int0, FloatNaN, StringEmpty, DateTime0 - appears in both forms, and every varint is
// exercised at the widths where its encoding changes size.
//
// Two suffixes mark cases whose bytes cannot simply be pinned:
//
//   .np  the bytes embed an assembly-qualified type name from this repository, so they
//        change whenever an assembly is renamed and say nothing about the format.
//   .rt  the bytes embed a corelib type name, which differs by runtime - mscorlib on
//        .NET Framework, System.Private.CoreLib on .NET. Every Type value is in this
//        group, and so are LinkedList, Queue, Stack and a nested Tuple, which write
//        their concrete type.
//
// Both are covered by round trip rather than by bytes, and the .rt group additionally by
// a test that reads the .NET Framework form on every runtime.

namespace Hyperlite.Persistence.Test.Serialization
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Runtime.Serialization;

    internal static class FormatCorpus
    {
        /// <summary>
        ///     One case: a name, the declared type to serialize as, and the value.
        /// </summary>
        /// <remarks>
        ///     The declared type matters and is not always the value's type - an int boxed
        ///     into an object is written differently from an int, because the reader has
        ///     nothing to infer the type from.
        /// </remarks>
        internal sealed class Case
        {
            internal Case(string name, Type type, object value)
            {
                this.Name = name;
                this.Type = type;
                this.Value = value;
            }

            internal string Name { get; private set; }

            internal Type Type { get; private set; }

            internal object Value { get; private set; }

            /// <summary>
            ///     Whether the bytes are independent of the assembly they were produced in,
            ///     and so can be pinned and compared across implementations.
            /// </summary>
            internal bool IsPortable
            {
                get
                {
                    return !this.Name.EndsWith(".np", StringComparison.Ordinal)
                           && !this.Name.EndsWith(".rt", StringComparison.Ordinal);
                }
            }

            /// <summary>
            ///     Whether the bytes depend on which runtime produced them.
            /// </summary>
            /// <remarks>
            ///     Anything that writes a corelib type name does: .NET Framework
            ///     names mscorlib and .NET writes System.Private.CoreLib. That is a real
            ///     compatibility question rather than a fixture inconvenience - a database
            ///     written on one and read on the other depends on the binder mapping the
            ///     name - and TestFormatCompatibility pins the .NET Framework form and
            ///     reads it back on every runtime to settle it.
            /// </remarks>
            internal bool IsRuntimeDependent
            {
                get { return this.Name.EndsWith(".rt", StringComparison.Ordinal); }
            }
        }

        /// <summary>
        ///     Every case, in a stable order.
        /// </summary>
        internal static IList<Case> Cases()
        {
            var cases = new List<Case>();

            Booleans(cases);
            SignedIntegers(cases);
            UnsignedIntegers(cases);
            Characters(cases);
            FloatingPoint(cases);
            Decimals(cases);
            DatesAndTimes(cases);
            Strings(cases);
            GuidsAndUris(cases);
            Types(cases);
            Enums(cases);
            Nullables(cases);
            Arrays(cases);
            Collections(cases);
            Dictionaries(cases);
            TuplesAndPairs(cases);
            References(cases);
            ObjectGraphs(cases);

            return cases;
        }

        private static void Add<T>(ICollection<Case> cases, string name, T value)
        {
            cases.Add(new Case(name, typeof(T), value));
        }

        //
        // True, False
        //

        private static void Booleans(ICollection<Case> cases)
        {
            Add(cases, "bool.true", true);
            Add(cases, "bool.false", false);
        }

        //
        // SByte, SByte0, Short, Short0, Int, Int0, Long, Long0
        //
        //
        // short, int and long are zigzag varints: the sign goes in the low bit, so the
        // encoding gains a byte at 63/64 rather than at 127/128. sbyte is not - it is one
        // raw byte, so -1 is ff and there is no boundary to find. The boundaries are what
        // a careless optimisation breaks and a round-trip test cannot see.

        private static void SignedIntegers(ICollection<Case> cases)
        {
            Add(cases, "sbyte.zero", (sbyte)0);
            Add(cases, "sbyte.one", (sbyte)1);
            Add(cases, "sbyte.minus.one", (sbyte)-1);
            Add(cases, "sbyte.63", (sbyte)63);
            Add(cases, "sbyte.64", (sbyte)64);
            Add(cases, "sbyte.minus.64", (sbyte)-64);
            Add(cases, "sbyte.minus.65", (sbyte)-65);
            Add(cases, "sbyte.min", sbyte.MinValue);
            Add(cases, "sbyte.max", sbyte.MaxValue);

            Add(cases, "short.zero", (short)0);
            Add(cases, "short.one", (short)1);
            Add(cases, "short.minus.one", (short)-1);
            Add(cases, "short.8191", (short)8191);
            Add(cases, "short.8192", (short)8192);
            Add(cases, "short.minus.8192", (short)-8192);
            Add(cases, "short.minus.8193", (short)-8193);
            Add(cases, "short.min", short.MinValue);
            Add(cases, "short.max", short.MaxValue);

            Add(cases, "int.zero", 0);
            Add(cases, "int.one", 1);
            Add(cases, "int.minus.one", -1);
            Add(cases, "int.63", 63);
            Add(cases, "int.64", 64);
            Add(cases, "int.8191", 8191);
            Add(cases, "int.8192", 8192);
            Add(cases, "int.1048575", 1048575);
            Add(cases, "int.1048576", 1048576);
            Add(cases, "int.134217727", 134217727);
            Add(cases, "int.134217728", 134217728);
            Add(cases, "int.min", int.MinValue);
            Add(cases, "int.max", int.MaxValue);

            Add(cases, "long.zero", 0L);
            Add(cases, "long.one", 1L);
            Add(cases, "long.minus.one", -1L);
            Add(cases, "long.63", 63L);
            Add(cases, "long.64", 64L);
            Add(cases, "long.int.max", (long)int.MaxValue);
            Add(cases, "long.int.min", (long)int.MinValue);
            Add(cases, "long.min", long.MinValue);
            Add(cases, "long.max", long.MaxValue);
        }

        //
        // Byte, Byte0, UShort, UShort0, UInt, UInt0, ULong, ULong0
        //
        //
        // ushort, uint and ulong are plain varints, no zigzag, so the width changes at
        // 127/128. byte is one raw byte, like sbyte.

        private static void UnsignedIntegers(ICollection<Case> cases)
        {
            Add(cases, "byte.zero", (byte)0);
            Add(cases, "byte.one", (byte)1);
            Add(cases, "byte.127", (byte)127);
            Add(cases, "byte.128", (byte)128);
            Add(cases, "byte.max", byte.MaxValue);

            Add(cases, "ushort.zero", (ushort)0);
            Add(cases, "ushort.one", (ushort)1);
            Add(cases, "ushort.127", (ushort)127);
            Add(cases, "ushort.128", (ushort)128);
            Add(cases, "ushort.16383", (ushort)16383);
            Add(cases, "ushort.16384", (ushort)16384);
            Add(cases, "ushort.max", ushort.MaxValue);

            Add(cases, "uint.zero", 0u);
            Add(cases, "uint.one", 1u);
            Add(cases, "uint.127", 127u);
            Add(cases, "uint.128", 128u);
            Add(cases, "uint.16383", 16383u);
            Add(cases, "uint.16384", 16384u);
            Add(cases, "uint.2097151", 2097151u);
            Add(cases, "uint.2097152", 2097152u);
            Add(cases, "uint.268435455", 268435455u);
            Add(cases, "uint.268435456", 268435456u);
            Add(cases, "uint.max", uint.MaxValue);

            Add(cases, "ulong.zero", 0UL);
            Add(cases, "ulong.one", 1UL);
            Add(cases, "ulong.uint.max", (ulong)uint.MaxValue);
            Add(cases, "ulong.max", ulong.MaxValue);
        }

        //
        // Char
        //

        private static void Characters(ICollection<Case> cases)
        {
            Add(cases, "char.nul", '\0');
            Add(cases, "char.a", 'a');
            Add(cases, "char.7f", '\u007f');
            Add(cases, "char.80", '\u0080');
            Add(cases, "char.7ff", '\u07ff');
            Add(cases, "char.800", '\u0800');
            Add(cases, "char.ffff", '\uffff');
            Add(cases, "char.high.surrogate", '\ud83d');
        }

        //
        // Float, Float0, FloatNaN, Double, Double0, ...
        //

        private static void FloatingPoint(ICollection<Case> cases)
        {
            Add(cases, "float.zero", 0.0f);
            Add(cases, "float.negative.zero", -0.0f);
            Add(cases, "float.one", 1.0f);
            Add(cases, "float.minus.one", -1.0f);
            Add(cases, "float.typical", 3.25f);
            Add(cases, "float.epsilon", float.Epsilon);
            Add(cases, "float.min", float.MinValue);
            Add(cases, "float.max", float.MaxValue);
            Add(cases, "float.nan", float.NaN);
            Add(cases, "float.positive.infinity", float.PositiveInfinity);
            Add(cases, "float.negative.infinity", float.NegativeInfinity);

            Add(cases, "double.zero", 0.0);
            Add(cases, "double.negative.zero", -0.0);
            Add(cases, "double.one", 1.0);
            Add(cases, "double.minus.one", -1.0);
            Add(cases, "double.pi", 3.141592653589793);
            Add(cases, "double.epsilon", double.Epsilon);
            Add(cases, "double.min", double.MinValue);
            Add(cases, "double.max", double.MaxValue);
            Add(cases, "double.nan", double.NaN);
            Add(cases, "double.positive.infinity", double.PositiveInfinity);
            Add(cases, "double.negative.infinity", double.NegativeInfinity);
        }

        //
        // Decimal
        //
        //
        // Decimal has no dedicated zero tag, so zero costs a full sixteen byte payload
        // like any other value. Pinned as it is, not as one might prefer.

        private static void Decimals(ICollection<Case> cases)
        {
            Add(cases, "decimal.zero", 0m);
            Add(cases, "decimal.one", 1m);
            Add(cases, "decimal.minus.one", -1m);
            Add(cases, "decimal.typical", 12345.6789m);
            Add(cases, "decimal.smallest", 0.0000000000000000000000000001m);
            Add(cases, "decimal.min", decimal.MinValue);
            Add(cases, "decimal.max", decimal.MaxValue);
            // trailing zeros are part of a decimal's representation, not its value
            Add(cases, "decimal.one.scaled", 1.000m);
        }

        //
        // DateTime0, DateTime, DateTimeOffset0, DateTimeOffset, TimeSpan, TimeSpan0
        //
        //
        // Kind travels with the value, so all three are pinned: a serializer that dropped
        // it would still round-trip within one process and lose the distinction on disk.

        private static void DatesAndTimes(ICollection<Case> cases)
        {
            Add(cases, "datetime.default", default(DateTime));
            Add(cases, "datetime.min", DateTime.MinValue);
            Add(cases, "datetime.max", DateTime.MaxValue);
            Add(cases, "datetime.utc", new DateTime(2026, 9, 26, 13, 45, 12, DateTimeKind.Utc));
            Add(cases, "datetime.local", new DateTime(2026, 9, 26, 13, 45, 12, DateTimeKind.Local));
            Add(cases, "datetime.unspecified",
                new DateTime(2026, 9, 26, 13, 45, 12, DateTimeKind.Unspecified));
            Add(cases, "datetime.with.ticks", new DateTime(637000000000000001L, DateTimeKind.Utc));

            Add(cases, "datetimeoffset.default", default(DateTimeOffset));
            Add(cases, "datetimeoffset.min", DateTimeOffset.MinValue);
            Add(cases, "datetimeoffset.max", DateTimeOffset.MaxValue);
            Add(cases, "datetimeoffset.plus.two",
                new DateTimeOffset(2026, 9, 26, 13, 45, 12, TimeSpan.FromHours(2)));
            Add(cases, "datetimeoffset.minus.five",
                new DateTimeOffset(2026, 9, 26, 13, 45, 12, TimeSpan.FromHours(-5)));
            Add(cases, "datetimeoffset.utc",
                new DateTimeOffset(2026, 9, 26, 13, 45, 12, TimeSpan.Zero));

            Add(cases, "timespan.zero", TimeSpan.Zero);
            Add(cases, "timespan.one.tick", new TimeSpan(1));
            Add(cases, "timespan.typical", TimeSpan.FromMilliseconds(1234567));
            Add(cases, "timespan.negative", TimeSpan.FromMilliseconds(-1234567));
            Add(cases, "timespan.min", TimeSpan.MinValue);
            Add(cases, "timespan.max", TimeSpan.MaxValue);
        }

        //
        // String, StringEmpty, Null
        //

        private static void Strings(ICollection<Case> cases)
        {
            Add(cases, "string.null", (string)null);
            Add(cases, "string.empty", string.Empty);
            Add(cases, "string.one.char", "a");
            Add(cases, "string.short", "sensor-01");
            Add(cases, "string.space", " ");
            Add(cases, "string.embedded.nul", "a\0b");
            Add(cases, "string.unicode", "temperatur-f\u00fchler \u00b5m \u2103");
            Add(cases, "string.surrogate.pair", "\ud83d\ude80");
            Add(cases, "string.127", new string('x', 127));
            Add(cases, "string.128", new string('x', 128));
            Add(cases, "string.long", new string('y', 300));
        }

        //
        // Guid, Uri
        //

        private static void GuidsAndUris(ICollection<Case> cases)
        {
            Add(cases, "guid.empty", Guid.Empty);
            Add(cases, "guid.typical", Guid.Parse("6c1e9a74-3b28-4d51-9f07-2ae84b3c6d15"));
            Add(cases, "guid.all.ff", new Guid(new byte[]
            {
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff
            }));

            Add(cases, "uri.null", (Uri)null);
            Add(cases, "uri.http", new Uri("http://example.invalid/a/b?c=d"));
            Add(cases, "uri.fragment", new Uri("https://example.invalid/a#b"));
            Add(cases, "uri.file", new Uri("file:///c:/temp/x.db"));
            Add(cases, "uri.relative", new Uri("a/b", UriKind.Relative));
            Add(cases, "uri.unicode", new Uri("http://example.invalid/f%C3%BChler"));
        }

        //
        // Type, TypeCode
        //

        private static void Types(ICollection<Case> cases)
        {
            Add(cases, "type.int.rt", typeof(int));
            Add(cases, "type.string.rt", typeof(string));
            Add(cases, "type.datetime.rt", typeof(DateTime));
            Add(cases, "type.object.rt", typeof(object));
            Add(cases, "type.int.array.rt", typeof(int[]));
            Add(cases, "type.nullable.int.rt", typeof(int?));
            Add(cases, "type.list.int.rt", typeof(List<int>));
            Add(cases, "type.dictionary.rt", typeof(Dictionary<string, int>));
        }

        //
        // Enums
        //
        //
        // An enum is written as its underlying value, so the underlying type is what the
        // bytes depend on.

        private static void Enums(ICollection<Case> cases)
        {
            Add(cases, "enum.int.zero", IntEnum.Zero);
            Add(cases, "enum.int", IntEnum.Wednesday);
            Add(cases, "enum.int.negative", IntEnum.Negative);
            Add(cases, "enum.byte", ByteEnum.Two);
            Add(cases, "enum.sbyte.negative", SByteEnum.MinusOne);
            Add(cases, "enum.short", ShortEnum.Large);
            Add(cases, "enum.long", LongEnum.Large);
            Add(cases, "enum.ulong.max", ULongEnum.Max);
            Add(cases, "enum.flags.none", FlagsEnum.None);
            Add(cases, "enum.flags.combined", FlagsEnum.A | FlagsEnum.C);
        }

        //
        // Nullable
        //

        private static void Nullables(ICollection<Case> cases)
        {
            Add(cases, "nullable.bool.null", (bool?)null);
            Add(cases, "nullable.bool.true", (bool?)true);
            Add(cases, "nullable.int.null", (int?)null);
            Add(cases, "nullable.int.zero", (int?)0);
            Add(cases, "nullable.int.set", (int?)42);
            Add(cases, "nullable.long.set", (long?)-1L);
            Add(cases, "nullable.double.nan", (double?)double.NaN);
            Add(cases, "nullable.decimal.set", (decimal?)1m);
            Add(cases, "nullable.datetime.default", (DateTime?)default(DateTime));
            Add(cases, "nullable.guid.empty", (Guid?)Guid.Empty);
            Add(cases, "nullable.enum.set", (IntEnum?)IntEnum.Wednesday);
            Add(cases, "nullable.char.set", (char?)'a');
            Add(cases, "nullable.timespan.zero", (TimeSpan?)TimeSpan.Zero);
        }

        //
        // Array, and the ArrayFlags
        //
        //
        // Rank goes in the flags, as does whether the elements carry their own tag - an
        // int[] does not need one per element, an object[] does.

        private static void Arrays(ICollection<Case> cases)
        {
            Add(cases, "array.null", (int[])null);
            Add(cases, "array.byte.empty", new byte[0]);
            Add(cases, "array.byte", new byte[] { 1, 2, 3, 0, 255 });
            Add(cases, "array.sbyte", new sbyte[] { -1, 0, 1, sbyte.MinValue, sbyte.MaxValue });
            Add(cases, "array.int.empty", new int[0]);
            Add(cases, "array.int", new[] { 0, 1, -1, int.MaxValue, int.MinValue });
            Add(cases, "array.long", new[] { 0L, -1L, long.MaxValue });
            Add(cases, "array.double", new[] { 0.0, double.NaN, 1.5 });
            Add(cases, "array.bool", new[] { true, false, true });
            Add(cases, "array.char", new[] { 'a', '\0', '\uffff' });
            Add(cases, "array.decimal", new[] { 0m, 1m, -1m });
            Add(cases, "array.guid", new[] { Guid.Empty });
            Add(cases, "array.datetime", new[] { default(DateTime), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            Add(cases, "array.string.empty", new string[0]);
            Add(cases, "array.string", new[] { "a", string.Empty, "a" });
            Add(cases, "array.string.with.null", new[] { "a", null, "b" });
            Add(cases, "array.nullable.int", new int?[] { 1, null, 0 });
            Add(cases, "array.enum", new[] { IntEnum.Zero, IntEnum.Wednesday });
            Add(cases, "array.object.mixed", new object[] { 1, "a", null, true, 2.5 });
            Add(cases, "array.object.empty", new object[0]);
            Add(cases, "array.type.rt", new[] { typeof(int), typeof(string) });
            Add(cases, "array.uri", new[] { new Uri("http://example.invalid/") });

            // rank, which lives in the flags
            Add(cases, "array.rank2", new[,] { { 1, 2, 3 }, { 4, 5, 6 } });
            Add(cases, "array.rank3", new[, ,] { { { 1, 2 } }, { { 3, 4 } } });
            Add(cases, "array.rank2.empty", new int[0, 0]);
            Add(cases, "array.rank2.string", new[,] { { "a", "b" } });

            // jagged is an array of arrays, not a rank
            Add(cases, "array.jagged", new[] { new[] { 1, 2 }, new[] { 3 } });
            Add(cases, "array.jagged.with.null", new[] { new[] { 1 }, null });
        }

        //
        // Collection, Enumerable, and the CollectionFlags
        //
        //
        // Typed, TypeTagged, Set and ValueTagged are all in play here: a List<int> needs
        // none of them per element, a List<object> needs a tag per value, a HashSet is a
        // set, and a non-generic list needs the element type written out.

        private static void Collections(ICollection<Case> cases)
        {
            Add(cases, "list.null", (List<int>)null);
            Add(cases, "list.int.empty", new List<int>());
            Add(cases, "list.int", new List<int> { 1, 2, 3 });
            Add(cases, "list.string", new List<string> { "a", null, string.Empty });
            Add(cases, "list.object", new List<object> { 1, "a", null });
            Add(cases, "list.nullable.int", new List<int?> { 1, null });
            Add(cases, "list.enum", new List<IntEnum> { IntEnum.Wednesday });

            Add(cases, "hashset.int", new HashSet<int> { 1 });
            Add(cases, "hashset.string", new HashSet<string> { "x" });
            Add(cases, "sortedset.int", new SortedSet<int> { 3, 1, 2 });

            Add(cases, "collection.int", new Collection<int> { 1, 2 });
            Add(cases, "observable.int", new ObservableCollection<int> { 1, 2 });
            Add(cases, "linkedlist.int.rt", new LinkedList<int>(new[] { 1, 2 }));
            Add(cases, "queue.int.rt", new Queue<int>(new[] { 1, 2 }));
            Add(cases, "stack.int.rt", new Stack<int>(new[] { 1, 2 }));

            // non-generic: the element type cannot be inferred and has to be written
            Add(cases, "arraylist", new ArrayList { 1, "a", null });

            // declared as an interface, so the concrete type has to be recorded
            Add(cases, "ilist.int", (IList<int>)new List<int> { 1, 2 });
            Add(cases, "icollection.int", (ICollection<int>)new List<int> { 1 });
            Add(cases, "iset.string", (ISet<string>)new HashSet<string> { "x" });

            // an enumerable that is not a collection
            Add(cases, "enumerable.int", Enumerable.Range(1, 3).Where(i => i > 0));
        }

        //
        // Dictionary, and the DictionaryFlags
        //

        private static void Dictionaries(ICollection<Case> cases)
        {
            Add(cases, "dictionary.null", (Dictionary<string, int>)null);
            Add(cases, "dictionary.empty", new Dictionary<string, int>());
            Add(cases, "dictionary.string.int", new Dictionary<string, int> { { "k", 1 } });
            Add(cases, "dictionary.int.string", new Dictionary<int, string> { { 1, "v" } });
            Add(cases, "dictionary.string.object", new Dictionary<string, object> { { "k", 1 } });
            Add(cases, "dictionary.object.object", new Dictionary<object, object> { { 1, "v" } });
            Add(cases, "dictionary.string.null.value", new Dictionary<string, string> { { "k", null } });
            Add(cases, "dictionary.enum.key", new Dictionary<IntEnum, int> { { IntEnum.Wednesday, 1 } });
            Add(cases, "sorteddictionary", new SortedDictionary<string, int> { { "a", 1 }, { "b", 2 } });
            Add(cases, "idictionary", (IDictionary<string, int>)new Dictionary<string, int> { { "k", 1 } });
            Add(cases, "hashtable", new Hashtable { { "k", 1 } });
            Add(cases, "dictionary.nested",
                new Dictionary<string, Dictionary<string, int>>
                {
                    { "outer", new Dictionary<string, int> { { "inner", 1 } } }
                });
        }

        //
        // Tuple, KeyValuePair
        //

        private static void TuplesAndPairs(ICollection<Case> cases)
        {
            Add(cases, "kvp.string.int", new KeyValuePair<string, int>("k", 1));
            Add(cases, "kvp.int.string", new KeyValuePair<int, string>(1, "v"));
            Add(cases, "kvp.null.value", new KeyValuePair<string, string>("k", null));
            Add(cases, "kvp.object", new KeyValuePair<object, object>(1, "v"));

            Add(cases, "tuple.1", Tuple.Create(1));
            Add(cases, "tuple.2", Tuple.Create(1, "t"));
            Add(cases, "tuple.3", Tuple.Create(1, "t", 2.5));
            Add(cases, "tuple.7", Tuple.Create((sbyte)1, (short)2, 3, 4L, 5.0f, 6.0, "s"));
            Add(cases, "tuple.nested.rt", Tuple.Create(Tuple.Create(1, 2), 3));
            Add(cases, "tuple.with.null", Tuple.Create((string)null, 1));
        }

        //
        // ObjectRef, StringRef, TypeRef, TypeSchema, TypeSchemaRef
        //
        //
        // The back-reference tags. A repeated string inside one object graph becomes a
        // StringRef; the same string repeated as array elements does not, because array
        // elements do not go through that path. Both are pinned, the second because it is
        // the format, not because it is desirable.

        private static void References(ICollection<Case> cases)
        {
            Add(cases, "stringref.array", new[] { "same", "same", "same" });
            Add(cases, "typeref.array.rt", new[] { typeof(int), typeof(int), typeof(int) });
            Add(cases, "typeref.mixed.rt", new[] { typeof(int), typeof(string), typeof(int) });
        }

        //
        // Object, SerializationInfo, and the schema tags
        //
        //
        // Everything from here embeds an assembly qualified type name, so it is marked .np
        // and compared by round trip rather than by bytes.

        private static void ObjectGraphs(ICollection<Case> cases)
        {
            Add(cases, "object.null.np", (Leaf)null);
            Add(cases, "object.flat.np", new Leaf { Id = 4711, Name = "sensor-01" });
            Add(cases, "object.default.np", new Leaf());
            Add(cases, "object.all.types.np", Wide.Populated());

            var shared = new Leaf { Id = 7, Name = "leaf" };
            Add(cases, "objectref.shared.np", new[] { shared, shared });
            Add(cases, "objectref.twice.np", new Pair { First = shared, Second = shared });

            var node = new Node { Name = "root" };
            node.Child = new Node { Name = "child", Parent = node };
            Add(cases, "objectref.cyclic.np", node);

            var self = new Node { Name = "self" };
            self.Parent = self;
            Add(cases, "objectref.self.np", self);

            // the same type twice in one graph: schema once, then a schema reference
            Add(cases, "typeschema.repeated.np", new[]
            {
                new Leaf { Id = 1, Name = "a" },
                new Leaf { Id = 2, Name = "b" }
            });

            // two different types, so two schemas
            Add(cases, "typeschema.two.np", new object[]
            {
                new Leaf { Id = 1, Name = "a" },
                new Node { Name = "n" }
            });

            // a repeated string inside a graph, which does become a StringRef
            Add(cases, "stringref.graph.np", new[]
            {
                new Leaf { Id = 1, Name = "same" },
                new Leaf { Id = 2, Name = "same" }
            });

            Add(cases, "object.inherited.np", new Derived { Id = 1, Name = "d", Extra = "e" });
            Add(cases, "object.with.collection.np",
                new Holder { Values = new List<int> { 1, 2 }, Map = new Dictionary<string, int> { { "k", 1 } } });
            Add(cases, "object.with.type.np", new TypeHolder { Which = typeof(int) });
            Add(cases, "object.struct.np", new Point { X = 1, Y = -1 });
            Add(cases, "serializable.np", new CustomSerializable(42, "s"));
        }

        //
        // corpus types
        //

        private enum IntEnum
        {
            Zero = 0,
            Wednesday = 3,
            Negative = -7
        }

        private enum ByteEnum : byte
        {
            Two = 2
        }

        private enum SByteEnum : sbyte
        {
            MinusOne = -1
        }

        private enum ShortEnum : short
        {
            Large = 12345
        }

        private enum LongEnum : long
        {
            Large = 1234567890123L
        }

        private enum ULongEnum : ulong
        {
            Max = ulong.MaxValue
        }

        [Flags]
        private enum FlagsEnum
        {
            None = 0,
            A = 1,
            B = 2,
            C = 4
        }

        private sealed class Leaf
        {
            public int Id { get; set; }

            public string Name { get; set; }
        }

        private class Base
        {
            public int Id { get; set; }

            public string Name { get; set; }
        }

        private sealed class Derived : Base
        {
            public string Extra { get; set; }
        }

        private sealed class Pair
        {
            public Leaf First { get; set; }

            public Leaf Second { get; set; }
        }

        private sealed class Node
        {
            public string Name { get; set; }

            public Node Child { get; set; }

            public Node Parent { get; set; }
        }

        private sealed class Holder
        {
            public List<int> Values { get; set; }

            public Dictionary<string, int> Map { get; set; }
        }

        private sealed class TypeHolder
        {
            public Type Which { get; set; }
        }

        private struct Point
        {
            public int X { get; set; }

            public int Y { get; set; }
        }

        /// <summary>
        ///     One property per value tag, so a single case covers the whole encoder table
        ///     inside an object rather than at the top level.
        /// </summary>
        private sealed class Wide
        {
            public bool Bool { get; set; }

            public sbyte SByte { get; set; }

            public byte Byte { get; set; }

            public short Short { get; set; }

            public ushort UShort { get; set; }

            public int Int { get; set; }

            public uint UInt { get; set; }

            public long Long { get; set; }

            public ulong ULong { get; set; }

            public char Char { get; set; }

            public float Float { get; set; }

            public double Double { get; set; }

            public decimal Decimal { get; set; }

            public DateTime DateTime { get; set; }

            public DateTimeOffset DateTimeOffset { get; set; }

            public TimeSpan TimeSpan { get; set; }

            public string String { get; set; }

            public Guid Guid { get; set; }

            public Uri Uri { get; set; }

            public IntEnum Enum { get; set; }

            public int? Nullable { get; set; }

            public static Wide Populated()
            {
                return new Wide
                {
                    Bool = true,
                    SByte = -1,
                    Byte = 2,
                    Short = -300,
                    UShort = 400,
                    Int = -1234567,
                    UInt = 3234567890u,
                    Long = -1234567890123456789L,
                    ULong = 12345678901234567890UL,
                    Char = 'q',
                    Float = 3.25f,
                    Double = 3.141592653589793,
                    Decimal = 12345.6789m,
                    DateTime = new DateTime(2026, 9, 26, 13, 45, 12, DateTimeKind.Utc),
                    DateTimeOffset =
                        new DateTimeOffset(2026, 9, 26, 13, 45, 12, TimeSpan.FromHours(2)),
                    TimeSpan = TimeSpan.FromMilliseconds(1234567),
                    String = "sensor-01",
                    Guid = Guid.Parse("6c1e9a74-3b28-4d51-9f07-2ae84b3c6d15"),
                    Uri = new Uri("http://example.invalid/a"),
                    Enum = IntEnum.Wednesday,
                    Nullable = 42
                };
            }
        }

        /// <summary>
        ///     Takes the ISerializable path, which writes a SerializationInfo rather than a
        ///     type schema.
        /// </summary>
        [Serializable]
        private sealed class CustomSerializable : ISerializable
        {
            public CustomSerializable(int number, string text)
            {
                this.Number = number;
                this.Text = text;
            }

            private CustomSerializable(SerializationInfo info, StreamingContext context)
            {
                this.Number = info.GetInt32("n");
                this.Text = info.GetString("t");
            }

            public int Number { get; private set; }

            public string Text { get; private set; }

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.AddValue("n", this.Number);
                info.AddValue("t", this.Text);
            }
        }
    }
}
