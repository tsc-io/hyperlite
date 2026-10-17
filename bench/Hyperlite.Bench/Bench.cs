// The Hyperlite benchmark: writes, scans and the maintenance operations, measured
// end to end through the native layer against a real database.
//
// It began as one half of a pair - the same source was compiled against ht4n as well,
// and the two runs were interleaved and diffed by a driver script. ht4n is not part of
// this repository, so the pair and the driver are gone and what remains stands on its
// own: run it, compare against the last run.
//
// Three numbers per case, and the third is the one to trust:
//
//   wall median   what a caller feels
//   wall minimum  the low-noise estimator of how fast the case can go, since
//                 contention only ever adds
//   cpu mean      processor time, which is what the work actually costs. The mean and
//                 not the minimum: it is quantised to the scheduler tick, so only an
//                 averaging estimator recovers a case shorter than one
//
// Wall clock is not usable for comparison on a machine that is doing anything else: a
// neighbour process stretches a case by tens of percent and does it unevenly across
// cases. Processor time barely moves when a core is stolen - the case simply takes
// longer in wall clock for the same cpu. This is also why this benchmark is not written
// on top of BenchmarkDotNet, which measures wall clock and has no cpu-time column.
//
// Allocated bytes per case are reported too, and they are the only fully deterministic
// column: they do not care what else the machine is doing, so an allocation regression
// is visible in a single run where a timing regression is not.
//
// Results go to stdout as TSV, one RESULT line per case.

namespace Hyperlite.Bench
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text;

    using Hyperlite;

    internal static class Bench
    {
        /// <summary>
        /// Cells per write case, and rows per scan case. Overridable, because the
        /// default is too small to read a few percent through the run-to-run noise:
        /// at 20k the cases take ~20 ms and the noise floor is ~2 ms, so anything under
        /// about 1.15x is unreadable. Raise it to attribute small differences.
        /// </summary>
        private static int n = 20000;

        /// <summary>
        /// Repetitions per case. Both the median and the minimum are reported; the
        /// minimum is the low-noise estimator for how fast the case can go, which is what
        /// makes a few percent between two libraries readable at all.
        /// </summary>
        private static int repetitions = 9;

        private const string Schema =
            "<Schema><AccessGroup name=\"default\">" +
            "<ColumnFamily><Name>a</Name></ColumnFamily>" +
            "<ColumnFamily><Name>b</Name></ColumnFamily>" +
            "</AccessGroup></Schema>";

        private static readonly List<string> Order = new List<string>();

        private static readonly Dictionary<string, List<double>> Timings =
            new Dictionary<string, List<double>>();

        /// <summary>
        /// Processor time per case, alongside the wall clock in <see cref="Timings" />.
        /// </summary>
        /// <remarks>
        /// Wall clock is what a caller feels, but it is not a usable way to compare two
        /// libraries on a machine that is doing anything else: on four cores a neighbour
        /// process stretches a case by tens of percent, which swamps the difference being
        /// looked for and does it unevenly across cases. Processor time is what the work
        /// actually costs and barely moves when a core is stolen - the case simply takes
        /// longer in wall clock for the same cpu.
        ///
        /// Its resolution is the scheduler tick, about 15 ms, so a single reading is
        /// coarse. That is the right trade here: quantisation is symmetric and averages
        /// out over repetitions, whereas contention only ever adds.
        /// </remarks>
        /// <summary>
        /// Wall time per repetition in the order measured, for --samples. <see cref="Timings" />
        /// is sorted in place for the summary, so it cannot serve.
        /// </summary>
        private static readonly Dictionary<string, List<double>> RawTimings =
            new Dictionary<string, List<double>>();

        private static readonly Dictionary<string, List<double>> Processor =
            new Dictionary<string, List<double>>();

        /// <summary>
        /// Bytes allocated per case. The deterministic column: unlike either clock it
        /// does not move with what else the machine is doing, so a regression here is
        /// readable from a single run.
        /// </summary>
        private static readonly Dictionary<string, List<double>> Allocated =
            new Dictionary<string, List<double>>();

        /// <summary>
        /// Operations each case performs per measurement, so cases that repeat different
        /// numbers of times can still be compared.
        /// </summary>
        /// <remarks>
        /// One for the store cases: each is a single pass over n cells, and n is in the
        /// header. The serialization cases set it, because a scalar is measured over
        /// twenty thousand values and an object graph over two thousand, and without this
        /// the two would print as though they cost the same.
        /// </remarks>
        private static readonly Dictionary<string, int> Operations =
            new Dictionary<string, int>();

        /// <summary>
        /// Substring a case name has to contain to run, or null for all of them.
        /// </summary>
        private static string caseFilter;

        /// <summary>
        /// Workload checks already reported; only the first repetition prints them.
        /// </summary>
        private static readonly HashSet<string> verified = new HashSet<string>();

        private static int Main(string[] args)
        {
            string filter = null;
            var printSamples = false;
            var tag = "run";
            var suite = "all";

            foreach (var arg in args)
            {
                if (arg == "-h" || arg == "--help")
                {
                    Usage();
                    return 0;
                }

                if (arg == "--format-golden")
                {
                    // writes the expected bytes in the form TestFormatCompatibility reads
                    FormatDigest.Golden();
                    return 0;
                }

                if (arg == "--format-digest")
                {
                    // measures nothing: prints the serialized form of a fixed corpus so a
                    // change to the serializer can be diffed against an earlier run
                    FormatDigest.Run();
                    return 0;
                }

                if (arg == "--samples")
                {
                    // every repetition's wall time as well as the summary, in run order
                    printSamples = true;
                }
                else if (arg.StartsWith("--n=", StringComparison.Ordinal))
                {
                    n = int.Parse(arg.Substring(4), CultureInfo.InvariantCulture);
                }
                else if (arg.StartsWith("--reps=", StringComparison.Ordinal))
                {
                    repetitions = int.Parse(arg.Substring(7), CultureInfo.InvariantCulture);
                }
                else if (arg.StartsWith("--case=", StringComparison.Ordinal))
                {
                    filter = arg.Substring(7);
                }
                else if (arg.StartsWith("--tag=", StringComparison.Ordinal))
                {
                    tag = arg.Substring(6);
                }
                else if (arg.StartsWith("--suite=", StringComparison.Ordinal))
                {
                    suite = arg.Substring(8);
                    if (suite != "store" && suite != "serialization" && suite != "entity"
                        && suite != "collections" && suite != "all")
                    {
                        Console.Error.WriteLine("unknown suite: " + suite);
                        return 2;
                    }
                }
                else
                {
                    Console.Error.WriteLine("unknown argument: " + arg);
                    Usage();
                    return 2;
                }
            }

            caseFilter = filter;

            // the tag only names the database file, so two runs in one directory - a
            // before and an after - cannot disturb each other
            var database = "bench-" + tag + ".db";

            AllocationCounter.Enable();

            Console.WriteLine("# assembly      : " + typeof(Context).Assembly.GetName().Name
                              + " " + FileVersionInfo.GetVersionInfo(typeof(Context).Assembly.Location).FileVersion);
            Console.WriteLine("# runtime       : " + Environment.Version
                              + (IntPtr.Size == 8 ? " x64" : " x86"));
            Console.WriteLine("# cells / rows  : " + n.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("# repetitions   : " + repetitions.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("# allocations   : " + (AllocationCounter.Available
                                                          ? "measured"
                                                          : "not available on this runtime"));
            if (filter != null)
            {
                Console.WriteLine("# cases matching: " + filter + " (the full workload still runs,");
                Console.WriteLine("#                 so these are full-run numbers)");
            }

            if (suite == "store" || suite == "all")
            {
                for (var rep = 0; rep < repetitions; ++rep)
                {
                    Cleanup(database);
                    RunOnce(database, rep == 0);
                }

                Cleanup(database);
            }

            if (suite == "entity" || suite == "all")
            {
                // Its own database file, so a store run and an entity run cannot disturb
                // each other's page cache when both are asked for
                var entityDatabase = database + ".entities";
                for (var rep = 0; rep < repetitions; ++rep)
                {
                    Cleanup(entityDatabase);
                    Entities.Run(entityDatabase);
                }

                Cleanup(entityDatabase);
            }

            if (suite == "serialization" || suite == "all")
            {
                // No database, so no repetition loop over one: each case repeats
                // internally and Measure is called once per case per pass.
                for (var rep = 0; rep < repetitions; ++rep)
                {
                    Serialization.Run();
                }
            }

            if (suite == "collections")
            {
                // not part of "all": it measures the collections, not the library, and
                // it would add minutes to every full run
                Collections.WarmUp();
                for (var rep = 0; rep < repetitions; ++rep)
                {
                    Collections.Run();
                }
            }

            if (Order.Count == 0)
            {
                Console.Error.WriteLine("no case matched '" + filter + "'");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("# case\twall.median.ms\twall.min.ms\tcpu.mean.ms\talloc.kb\tops\tns.per.op\talloc.b.per.op");
            foreach (var name in Order)
            {
                var samples = Timings[name];
                samples.Sort();
                var cpu = Processor[name];
                cpu.Sort();
                var allocated = Allocated[name];
                allocated.Sort();

                var wallMin = samples[0];
                var allocMedian = allocated[allocated.Count / 2];

                // A case only gets per-operation figures when it declared how many
                // operations it performs. A store case is one pass over n cells, so
                // dividing by one would restate its total in nanoseconds and read like a
                // per-cell cost, which it is not.
                var declared = Operations.ContainsKey(name);
                var ops = declared ? Operations[name] : 1;
                var perOp = declared
                                ? (wallMin * 1000000.0 / ops).ToString("F1", CultureInfo.InvariantCulture)
                                : "-";
                var allocPerOp = declared
                                     ? (allocMedian / ops).ToString("F0", CultureInfo.InvariantCulture)
                                     : "-";

                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "RESULT\t{0}\t{1:F2}\t{2:F2}\t{3:F2}\t{4:F1}\t{5}\t{6}\t{7}",
                    name,
                    samples[samples.Count / 2],
                    wallMin,
                    Mean(cpu),
                    allocMedian / 1024.0,
                    declared ? ops.ToString(CultureInfo.InvariantCulture) : "-",
                    perOp,
                    allocPerOp));
            }

            // Memory the run cost the process, which is the other half of "is this
            // allocator worth it": an allocator can be neutral on time and still change
            // the footprint, in either direction.
            //
            // Peak rather than current, because the run has already released most of what
            // it took by the time this prints. Both counters are the operating system's
            // view of the whole process, so they include the native heap - which the
            // per-case alloc.kb column cannot see, that being managed allocation only.
            if (printSamples)
            {
                Console.WriteLine();
                Console.WriteLine("# case	repetition	wall.ms");
                foreach (var name in Order)
                {
                    // Timings was sorted for the summary above; RawTimings keeps run order
                    var raw = RawTimings[name];
                    for (var i = 0; i < raw.Count; ++i)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "SAMPLE	{0}	{1}	{2:F4}", name, i, raw[i]));
                    }
                }
            }

            var self = Process.GetCurrentProcess();
            Console.WriteLine();
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "PEAK\tworking.set.mb\t{0:F1}",
                self.PeakWorkingSet64 / (1024.0 * 1024.0)));
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "PEAK\tpaged.mb\t{0:F1}",
                self.PeakPagedMemorySize64 / (1024.0 * 1024.0)));

            return 0;
        }

        /// <summary>
        /// Arithmetic mean, which is the right estimator for the processor time samples.
        /// </summary>
        /// <remarks>
        /// Not the minimum, which is what wall clock uses and what this column used to
        /// use. Processor time is quantised to the scheduler tick, about 15 ms, so a case
        /// that costs 10 ms reads either 0 or 15.6 on any given repetition. Quantisation
        /// is symmetric and therefore averages out - but only under an estimator that
        /// averages. The minimum does the opposite: it picks the low end every time, and
        /// reported 0.00 for any case shorter than a tick.
        ///
        /// Wall clock keeps the minimum, for the opposite reason: contention only ever
        /// adds, so there the lowest sample is the cleanest one.
        /// </remarks>
        private static double Mean(List<double> samples)
        {
            var total = 0.0;
            foreach (var sample in samples)
            {
                total += sample;
            }

            return total / samples.Count;
        }

        private static void Usage()
        {
            Console.WriteLine("Hyperlite benchmark");
            Console.WriteLine();
            Console.WriteLine("  --n=<count>     cells per write case, rows per scan case (default 20000)");
            Console.WriteLine("  --reps=<count>  repetitions per case (default 9)");
            Console.WriteLine("  --case=<text>   report only the cases whose name contains this. Every");
            Console.WriteLine("                  case still runs - the scans read what the writes wrote -");
            Console.WriteLine("                  so this narrows the output, not the time taken");
            Console.WriteLine("  --tag=<name>    names the database file, so two runs do not collide");
            Console.WriteLine("  --format-digest prints the serialized form of a fixed corpus as hex and");
            Console.WriteLine("                  exits. Diff two runs across a serializer change to prove");
            Console.WriteLine("                  the format did not move");
            Console.WriteLine("  --samples       also print every repetition's wall time, in run order");
            Console.WriteLine("  --suite=<name>  store, entity, serialization, collections or all");
            Console.WriteLine("                  (default all). store measures the client, entity the");
            Console.WriteLine("                  object mapper, serialization needs no database at all,");
            Console.WriteLine("                  and collections - not in all - sets the flat hash");
            Console.WriteLine("                  collections against the framework's");
            Console.WriteLine();
            Console.WriteLine("Compare cpu.mean.ms between runs; wall clock is not comparable on a");
            Console.WriteLine("machine that is doing anything else. alloc.kb is deterministic.");
        }

        private static void RunOnce(string database, bool warmup)
        {
            using (var context = Context.Create("Provider=SQLite; Ht4n.SQLite.Filename=" + database))
            using (var client = context.CreateClient())
            {
                client.CreateNamespace("bench", CreateDispositions.CreateIfNotExist);
                using (var ns = client.OpenNamespace("bench"))
                {
                    ns.CreateTable("t", Schema, CreateDispositions.CreateIfNotExist);

                    using (var table = ns.OpenTable("t"))
                    {
                        if (warmup)
                        {
                            // one untimed pass, so the JIT and the page cache are warm
                            // before anything is measured
                            WriteSingle(table, 1000, "w");
                            using (var scanner = table.CreateScanner())
                            {
                                foreach (var cell in scanner)
                                {
                                    GC.KeepAlive(cell);
                                }
                            }

                            using (var mutator = table.CreateMutator())
                            {
                                for (var i = 0; i < 1000; ++i)
                                {
                                    mutator.Delete("w" + i.ToString("D7", CultureInfo.InvariantCulture));
                                }
                            }
                        }

                        Measure("write.single", () => WriteSingle(table, n, "s"));
                        Verify(table, "after write.single", n);
                        Measure("write.batched", () => WriteBatched(table, n, "t"));
                        Measure("write.chunked", () => WriteChunked(table, n, "u"));
                        Measure("write.queued", () => WriteQueued(table, n, "v"));
                        Verify(table, "after the writes", 4 * n);

                        // the reads all run over the 4N cells the writes just made
                        Measure("scan.full.next", () => ScanNext(table));
                        Measure("scan.full.move", () => ScanMove(table));
                        Measure("scan.full.buffered", () => ScanBuffered(table));
                        Measure("scan.full.enumerate", () => ScanEnumerate(table));
                        Measure("scan.keysonly", () => ScanKeysOnly(table));
                        Measure("scan.column", () => ScanColumn(table));
                        Measure("scan.rowregex", () => ScanRowRegex(table));
                        Measure("scan.valueregex", () => ScanValueRegex(table));
                        Measure("scan.rowlookup", () => ScanRowLookups(table, n / 10, "s"));
                        Measure("scan.rowinterval", () => ScanRowIntervals(table, n / 40, "s"));

                        Measure("delete.single", () => DeleteSingle(table, n, "v"));
                        Verify(table, "after delete.single", 3 * n);
                    }

                    Measure("schema.churn", () => SchemaChurn(ns, 200));
                }

                Measure("key.generate", () => KeyGenerate(10 * n));
                Measure("namespace.churn", () => NamespaceChurn(client, 200));
            }
        }

        //
        // cases
        //

        private static void WriteSingle(ITable table, int count, string prefix)
        {
            using (var mutator = table.CreateMutator())
            {
                var key = new Key { ColumnFamily = "a" };
                var value = Encoding.UTF8.GetBytes("0123456789012345678901234567890123456789");
                for (var i = 0; i < count; ++i)
                {
                    key.Row = prefix + i.ToString("D7", CultureInfo.InvariantCulture);
                    mutator.Set(key, value);
                }
            }
        }

        private static void WriteBatched(ITable table, int count, string prefix)
        {
            var value = Encoding.UTF8.GetBytes("0123456789012345678901234567890123456789");
            var cells = new List<Cell>(count);
            for (var i = 0; i < count; ++i)
            {
                cells.Add(new Cell(
                    new Key(prefix + i.ToString("D7", CultureInfo.InvariantCulture), "a"), value));
            }

            using (var mutator = table.CreateMutator())
            {
                mutator.Set(cells);
            }
        }

        private static void WriteChunked(ITable table, int count, string prefix)
        {
            using (var mutator = table.CreateMutator(MutatorSpec.CreateChunked()))
            {
                var value = Encoding.UTF8.GetBytes("0123456789012345678901234567890123456789");
                for (var i = 0; i < count; ++i)
                {
                    mutator.Set(
                        new Key(prefix + i.ToString("D7", CultureInfo.InvariantCulture), "a"), value);
                }
            }
        }

        private static void WriteQueued(ITable table, int count, string prefix)
        {
            using (var mutator = table.CreateMutator(MutatorSpec.CreateQueued()))
            {
                var value = Encoding.UTF8.GetBytes("0123456789012345678901234567890123456789");
                for (var i = 0; i < count; ++i)
                {
                    mutator.Set(
                        new Key(prefix + i.ToString("D7", CultureInfo.InvariantCulture), "a"), value);
                }
            }
        }

        private static void ScanNext(ITable table)
        {
            using (var scanner = table.CreateScanner())
            {
                Cell cell;
                var n = 0;
                while (scanner.Next(out cell))
                {
                    n += cell.ValueLength;
                }

                GC.KeepAlive(n);
            }
        }

        private static void ScanMove(ITable table)
        {
            using (var scanner = table.CreateScanner())
            {
                var cell = new Cell();
                var n = 0;
                while (scanner.Move(cell))
                {
                    n += cell.ValueLength;
                }

                GC.KeepAlive(n);
            }
        }

        private static void ScanBuffered(ITable table)
        {
            using (var scanner = table.CreateScanner())
            {
                var cell = new BufferedCell(256);
                var n = 0;
                while (scanner.Move(cell))
                {
                    n += cell.ValueLength;
                }

                GC.KeepAlive(n);
            }
        }

        private static void ScanEnumerate(ITable table)
        {
            using (var scanner = table.CreateScanner())
            {
                var n = 0;
                foreach (var cell in scanner)
                {
                    n += cell.ValueLength;
                }

                GC.KeepAlive(n);
            }
        }

        private static void ScanKeysOnly(ITable table)
        {
            using (var scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }))
            {
                var cell = new Cell();
                var n = 0;
                while (scanner.Move(cell))
                {
                    ++n;
                }

                GC.KeepAlive(n);
            }
        }

        private static void ScanColumn(ITable table)
        {
            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("a")))
            {
                var cell = new Cell();
                var n = 0;
                while (scanner.Move(cell))
                {
                    ++n;
                }

                GC.KeepAlive(n);
            }
        }

        private static void ScanRowRegex(ITable table)
        {
            using (var scanner = table.CreateScanner(new ScanSpec { RowRegex = "^s00001" }))
            {
                var cell = new Cell();
                var n = 0;
                while (scanner.Move(cell))
                {
                    ++n;
                }

                GC.KeepAlive(n);
            }
        }

        private static void ScanValueRegex(ITable table)
        {
            using (var scanner = table.CreateScanner(new ScanSpec { ValueRegex = "89$" }))
            {
                var cell = new Cell();
                var n = 0;
                while (scanner.Move(cell))
                {
                    ++n;
                }

                GC.KeepAlive(n);
            }
        }

        private static void ScanRowLookups(ITable table, int count, string prefix)
        {
            var cell = new Cell();
            for (var i = 0; i < count; ++i)
            {
                var row = prefix + i.ToString("D7", CultureInfo.InvariantCulture);
                using (var scanner = table.CreateScanner(new ScanSpec(row)))
                {
                    while (scanner.Move(cell))
                    {
                    }
                }
            }
        }

        private static void ScanRowIntervals(ITable table, int count, string prefix)
        {
            var cell = new Cell();
            for (var i = 0; i < count; ++i)
            {
                var from = prefix + (i * 10).ToString("D7", CultureInfo.InvariantCulture);
                var to = prefix + ((i * 10) + 9).ToString("D7", CultureInfo.InvariantCulture);
                using (var scanner = table.CreateScanner(new ScanSpec(new RowInterval(from, to))))
                {
                    while (scanner.Move(cell))
                    {
                    }
                }
            }
        }

        private static void DeleteSingle(ITable table, int count, string prefix)
        {
            using (var mutator = table.CreateMutator())
            {
                for (var i = 0; i < count; ++i)
                {
                    mutator.Delete(prefix + i.ToString("D7", CultureInfo.InvariantCulture));
                }
            }
        }

        private static void SchemaChurn(INamespace ns, int count)
        {
            for (var i = 0; i < count; ++i)
            {
                var name = "churn-" + i.ToString(CultureInfo.InvariantCulture);
                ns.CreateTable(name, Schema);
                ns.GetTableSchema(name, true);
                ns.DropTable(name);
            }
        }

        private static void NamespaceChurn(IClient client, int count)
        {
            for (var i = 0; i < count; ++i)
            {
                var name = "ns-" + i.ToString(CultureInfo.InvariantCulture);
                client.CreateNamespace(name);
                client.NamespaceExists(name);
                client.DropNamespace(name);
            }
        }

        private static void KeyGenerate(int count)
        {
            var sink = 0;
            for (var i = 0; i < count; ++i)
            {
                sink += Key.Generate().Length;
            }

            GC.KeepAlive(sink);
        }

        /// <summary>
        /// Counts the cells in the table and fails loudly on a mismatch.
        /// </summary>
        /// <remarks>
        /// Cheap insurance: a benchmark whose workload silently shrank on one side would
        /// produce numbers that look great and mean nothing.
        /// </remarks>
        private static void Verify(ITable table, string where, int expected)
        {
            if (verified.Contains(where))
            {
                return;
            }

            verified.Add(where);

            var n = 0;
            using (var scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }))
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    ++n;
                }
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "# verify       : {0} = {1} cells{2}",
                where,
                n,
                n == expected ? string.Empty : " MISMATCH, expected " + expected));

            if (n != expected)
            {
                Environment.Exit(2);
            }
        }

        //
        // harness
        //

        /// <summary>
        /// Measures one case. Internal so the serialization suite in Serialization.cs can
        /// register its cases through the same counters and get the same three numbers.
        /// </summary>
        internal static void MeasureCase(string name, Action action)
        {
            Measure(name, action);
        }

        /// <summary>
        /// Measures a case that performs <paramref name="operations" /> operations, so the
        /// report can give a cost per operation.
        /// </summary>
        internal static void MeasureCase(string name, int operations, Action action)
        {
            Operations[name] = operations;
            Measure(name, action);
        }

        private static void Measure(string name, Action action)
        {
            // A case the filter excludes still runs, it is just not recorded.
            //
            // It has to: the cells the scan cases read are the ones the write cases
            // wrote, so skipping the writes would have the scans measure an empty table -
            // fast, and meaningless. Filtering the workload rather than the output would
            // also invalidate every Verify below, whose counts are the cumulative effect
            // of the whole sequence.
            //
            // So --case= narrows what is printed and costs a full run's time. That is the
            // right trade: a number that took longer to get is still a number, a number
            // measured against the wrong workload is not.
            var record = caseFilter == null
                         || name.IndexOf(caseFilter, StringComparison.OrdinalIgnoreCase) >= 0;

            if (!record)
            {
                action();
                return;
            }

            if (!Timings.ContainsKey(name))
            {
                Timings[name] = new List<double>();
                RawTimings[name] = new List<double>();
                Processor[name] = new List<double>();
                Allocated[name] = new List<double>();
                Order.Add(name);
            }

            // a settled heap before each case, so one case's garbage is not another's
            // pause
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var self = Process.GetCurrentProcess();
            var cpu = self.TotalProcessorTime;
            var allocated = AllocationCounter.Read();
            var watch = Stopwatch.StartNew();
            action();
            watch.Stop();
            var spentAllocating = AllocationCounter.Read() - allocated;
            var spent = Process.GetCurrentProcess().TotalProcessorTime - cpu;

            Timings[name].Add(watch.Elapsed.TotalMilliseconds);
            RawTimings[name].Add(watch.Elapsed.TotalMilliseconds);
            Processor[name].Add(spent.TotalMilliseconds);
            Allocated[name].Add(spentAllocating);
        }

        private static void Cleanup(string database)
        {
            foreach (var suffix in new[] { string.Empty, "-journal", "-wal", "-shm" })
            {
                var path = database + suffix;
                if (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                        // a stale handle is unusual but not worth aborting the run for
                    }
                }
            }
        }
    }
}
