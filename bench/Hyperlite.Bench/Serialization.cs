// The serializer on its own: no database, no native layer, no p/invoke.
//
// Hyperlite.Persistence's format is not a uniform "write the bytes" encoder. It has a tag
// per shape *and* per notable value, so the cost of writing a value depends on which value
// it is:
//
//   Int / Int0, SByte0, Short0, Long0 ...   a zero takes a tag and no payload
//   Float0 / FloatNaN, Double0 / DoubleNaN  likewise for the two interesting floats
//   DateTime0, TimeSpan0, StringEmpty       and for default and empty
//   True / False versus Bool                a bool is the tag
//   StringRef, TypeRef, ObjectRef           a repeat is a back reference, not a copy
//
// So every scalar is measured twice, at a typical value and at the value with its own
// tag, and the reference tags get cases that repeat a value against cases that do not.
// That is what makes the numbers useful: a change to the encoder that helps the common
// path and hurts the zero path is visible here and invisible in a single average.
//
// Serialize and deserialize are separate cases throughout. They are not symmetric - the
// writer sizes and grows a buffer, the reader walks tags and allocates results - and a
// regression in one says nothing about the other.

namespace Hyperlite.Bench
{
    using System;
    using System.Collections.Generic;

    using Hyperlite.Persistence.Serialization;

    internal static class Serialization
    {
        /// <summary>
        /// Values per case. Each is serialized and deserialized this many times, so the
        /// case is long enough to read against the scheduler tick.
        /// </summary>
        private const int Iterations = 20000;

        private const string Short = "sensor-01";

        private const string Long =
            "a considerably longer value, long enough that the encoder writes a length " +
            "prefix and copies rather than taking any short string path, and long enough " +
            "that the difference between ascii and utf8 handling can be seen at all";

        private const string Unicode = "temperatur-fühler µm ℃ 温度";

        internal static void Run()
        {
            Scalars();
            Strings();
            Arrays();
            Collections();
            Objects();
            References();
        }

        /// <summary>
        /// Every scalar the format has a tag for, at a typical value and at the value the
        /// encoder has a dedicated tag for.
        /// </summary>
        private static void Scalars()
        {
            RoundTrip("ser.bool.true", true);
            RoundTrip("ser.bool.false", false);

            RoundTrip("ser.sbyte", (sbyte)-42);
            RoundTrip("ser.sbyte.zero", (sbyte)0);
            RoundTrip("ser.byte", (byte)200);
            RoundTrip("ser.byte.zero", (byte)0);
            RoundTrip("ser.short", (short)-12345);
            RoundTrip("ser.short.zero", (short)0);
            RoundTrip("ser.ushort", (ushort)54321);
            RoundTrip("ser.ushort.zero", (ushort)0);
            RoundTrip("ser.int", -1234567);
            RoundTrip("ser.int.zero", 0);
            RoundTrip("ser.uint", 3234567890u);
            RoundTrip("ser.uint.zero", 0u);
            RoundTrip("ser.long", -1234567890123456789L);
            RoundTrip("ser.long.zero", 0L);
            RoundTrip("ser.ulong", 12345678901234567890UL);
            RoundTrip("ser.ulong.zero", 0UL);

            RoundTrip("ser.char", 'q');
            RoundTrip("ser.float", 3.25f);
            RoundTrip("ser.float.zero", 0.0f);
            RoundTrip("ser.float.nan", float.NaN);
            RoundTrip("ser.double", 3.141592653589793);
            RoundTrip("ser.double.zero", 0.0);
            RoundTrip("ser.double.nan", double.NaN);
            RoundTrip("ser.decimal", 12345.6789m);
            RoundTrip("ser.decimal.zero", 0m);

            RoundTrip("ser.datetime", new DateTime(2026, 9, 26, 13, 45, 12, DateTimeKind.Utc));
            RoundTrip("ser.datetime.default", default(DateTime));
            RoundTrip("ser.datetimeoffset",
                      new DateTimeOffset(2026, 9, 26, 13, 45, 12, TimeSpan.FromHours(2)));
            RoundTrip("ser.datetimeoffset.default", default(DateTimeOffset));
            RoundTrip("ser.timespan", TimeSpan.FromMilliseconds(1234567));
            RoundTrip("ser.timespan.zero", TimeSpan.Zero);

            RoundTrip("ser.guid", Guid.Parse("6c1e9a74-3b28-4d51-9f07-2ae84b3c6d15"));
            RoundTrip("ser.uri", new Uri("http://example.invalid/a/b?c=d"));

            // nullable, both states, because the null case is a single tag
            RoundTrip("ser.nullable.set", (int?)42);
            RoundTrip("ser.nullable.null", (int?)null);

            RoundTrip("ser.enum", DayOfWeek.Wednesday);
        }

        private static void Strings()
        {
            RoundTrip("ser.string.short", Short);
            RoundTrip("ser.string.empty", string.Empty);
            RoundTrip("ser.string.long", Long);
            RoundTrip("ser.string.unicode", Unicode);
        }

        private static void Arrays()
        {
            var bytes = new byte[1024];
            for (var i = 0; i < bytes.Length; ++i)
            {
                bytes[i] = (byte)i;
            }

            var ints = new int[256];
            for (var i = 0; i < ints.Length; ++i)
            {
                ints[i] = i * 7;
            }

            var doubles = new double[256];
            for (var i = 0; i < doubles.Length; ++i)
            {
                doubles[i] = i * 1.5;
            }

            var strings = new string[64];
            for (var i = 0; i < strings.Length; ++i)
            {
                strings[i] = Short + i.ToString();
            }

            RoundTrip("ser.array.byte.1k", bytes, 200);
            RoundTrip("ser.array.int.256", ints, 200);
            RoundTrip("ser.array.double.256", doubles, 200);
            RoundTrip("ser.array.string.64", strings, 200);
            RoundTrip("ser.array.empty", new int[0]);
        }

        private static void Collections()
        {
            var list = new List<int>();
            var set = new HashSet<string>();
            var map = new Dictionary<string, int>();
            for (var i = 0; i < 64; ++i)
            {
                list.Add(i);
                set.Add(Short + i.ToString());
                map[Short + i.ToString()] = i;
            }

            RoundTrip("ser.list.int.64", list, 400);
            RoundTrip("ser.hashset.string.64", set, 400);
            RoundTrip("ser.dictionary.64", map, 400);
            RoundTrip("ser.keyvaluepair", new KeyValuePair<string, int>(Short, 7));
            RoundTrip("ser.tuple", Tuple.Create(1, Short, 2.5));
        }

        private static void Objects()
        {
            RoundTrip("ser.object.flat", Reading.Sample());
            RoundTrip("ser.object.nested", Station.Sample(), 2000);
        }

        /// <summary>
        /// The back reference tags: a repeated string, type or object is written once and
        /// referred to afterwards, so a payload full of repeats should cost markedly less
        /// than the same payload with everything distinct.
        /// </summary>
        private static void References()
        {
            var repeated = new string[64];
            for (var i = 0; i < repeated.Length; ++i)
            {
                repeated[i] = Short;                       // one string, sixty-four slots
            }

            var distinct = new string[64];
            for (var i = 0; i < distinct.Length; ++i)
            {
                distinct[i] = Short + i.ToString();
            }

            RoundTrip("ser.stringref.repeated", repeated, 400);
            RoundTrip("ser.stringref.distinct", distinct, 400);

            var shared = Reading.Sample();
            var sharing = new object[16];
            for (var i = 0; i < sharing.Length; ++i)
            {
                sharing[i] = shared;                       // one object, sixteen slots
            }

            RoundTrip("ser.objectref.shared", sharing, 1000);

            var cyclic = Station.Sample();
            cyclic.Readings[0].Station = cyclic;            // a cycle, so ObjectRef closes it
            RoundTrip("ser.objectref.cyclic", cyclic, 2000);
        }

        /// <summary>
        /// Measures serialize and deserialize as two cases, at the given iteration count.
        /// </summary>
        private static void RoundTrip<T>(string name, T value, int iterations = Iterations)
        {
            // one untimed pass, so the type is inspected and its schema built before
            // anything is measured - otherwise the first case to touch a type pays for
            // every later one
            var warm = Serializer.ToByteArray(value);
            GC.KeepAlive(Deserializer.FromByteArray<T>(warm));

            Bench.MeasureCase(name + ".write", iterations, () =>
            {
                for (var i = 0; i < iterations; ++i)
                {
                    GC.KeepAlive(Serializer.ToByteArray(value));
                }
            });

            var serialized = Serializer.ToByteArray(value);
            Bench.MeasureCase(name + ".read", iterations, () =>
            {
                for (var i = 0; i < iterations; ++i)
                {
                    GC.KeepAlive(Deserializer.FromByteArray<T>(serialized));
                }
            });
        }

        private sealed class Reading
        {
            public int Id { get; set; }

            public string Sensor { get; set; }

            public double Value { get; set; }

            public DateTime At { get; set; }

            public bool Valid { get; set; }

            public Station Station { get; set; }

            internal static Reading Sample()
            {
                return new Reading
                {
                    Id = 4711,
                    Sensor = Short,
                    Value = 21.75,
                    At = new DateTime(2026, 9, 26, 13, 45, 12, DateTimeKind.Utc),
                    Valid = true
                };
            }
        }

        private sealed class Station
        {
            public string Name { get; set; }

            public Guid Key { get; set; }

            public List<Reading> Readings { get; set; }

            internal static Station Sample()
            {
                var station = new Station
                {
                    Name = "station-alpha",
                    Key = Guid.Parse("2d587f25-c918-4c14-b94f-0c2cd2e19e17"),
                    Readings = new List<Reading>()
                };

                for (var i = 0; i < 8; ++i)
                {
                    var reading = Reading.Sample();
                    reading.Id = i;
                    station.Readings.Add(reading);
                }

                return station;
            }
        }
    }
}
