namespace Hyperlite.Test
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     What the fixtures in this suite share: the connection string read from the app
    ///     config, the context, client and namespace built from it, and the helpers that
    ///     create and empty a table.
    /// </summary>
    [TestClass]
    public abstract class TestBase
    {
        private static IClient client;

        private static IContext context;

        private static INamespace ns;

        protected static IClient Client
        {
            get
            {
                return client ?? (client = Context.CreateClient());
            }
        }

        protected static string ConnectionString { get; private set; }

        protected static IContext Context
        {
            get
            {
                return context ?? (context = Hyperlite.Context.Create(ConnectionString));
            }
        }

        protected static INamespace Ns
        {
            get
            {
                return ns ?? (ns = Client.OpenNamespace(NsName, OpenDispositions.OpenAlways));
            }
        }

        /// <summary>
        /// Gets the namespace name.
        /// </summary>
        protected static string NsName { get; private set; }

        /// <summary>
        /// Assembly clean up, drop 'test' namespace.
        /// </summary>
        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            if (ns != null)
            {
                ns.Dispose();
                ns = null;
            }

            if (client != null)
            {
                client.DropNamespace(NsName, DropDispositions.Complete);
                client.Dispose();
            }

            if (context != null)
            {
                context.Dispose();
            }
        }

        /// <summary>
        /// Initialize the assembly, read the appconfig.
        /// </summary>
        /// <param name = "testContext">The test context.</param>
        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext testContext)
        {
            // test.cmd names a scratch directory under build, as it runs the smoke harness from
            // one: the databases the tests create, relative to the working directory, go there
            // rather than among the binaries in dist
            var scratch = Environment.GetEnvironmentVariable("HYPERLITE_TEST_SCRATCH");
            if (!string.IsNullOrEmpty(scratch))
            {
                Directory.CreateDirectory(scratch);
                Directory.SetCurrentDirectory(scratch);
            }

            ConnectionString = ConfigurationManager.AppSettings["ConnectionString"].Trim();
            NsName = ConfigurationManager.AppSettings["Namespace"].Trim();
            Assert.IsFalse(string.IsNullOrEmpty(NsName));
            Assert.AreNotEqual(NsName, "/"); // avoid using root namespace
            Logging.Logfile = Assembly.GetAssembly(typeof(TestBase)).Location + ".log";
            Logging.LogMessagePublished = message => Trace.WriteLine(message);
        }

        /// <summary>
        /// Preventing the system from entering sleep.
        /// </summary>
        protected static void ContinueExecution()
        {
            if (Path.DirectorySeparatorChar == '\\')
            {
                NativeMethods.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
            }
        }

        /// <summary>
        /// Deletes all cells in the table specified.
        /// </summary>
        /// <param name = "table">Table.</param>
        protected static void Delete(ITable table)
        {
            using (var scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }))
            {
                using (var mutator = table.CreateMutator())
                {
                    var cell = new Cell();
                    while (scanner.Move(cell))
                    {
                        mutator.Delete(cell.Key);
                    }
                }
            }

            var c = 0;
            using (var scanner = table.CreateScanner())
            {
                var cell = new Cell();
                while (scanner.Move(cell))
                {
                    ++c;
                }
            }

            Assert.AreEqual(0, c);
        }

        /// <summary>
        /// Deletes all cells in the table, column family specified.
        /// </summary>
        /// <param name = "table">Table.</param>
        /// <param name = "cf">Column family.</param>
        protected static void DeleteColumnFamily(ITable table, string cf)
        {
            var scanSpec = new ScanSpec { KeysOnly = true }.AddColumn(cf);
            using (var scanner = table.CreateScanner(scanSpec))
            {
                using (var mutator = table.CreateMutator())
                {
                    var cell = new Cell();
                    while (scanner.Move(cell))
                    {
                        mutator.Delete(cell.Key);
                    }
                }
            }
        }

        /// <summary>
        /// Drop namespaces from 'test' namespace.
        /// </summary>
        /// <param name = "regex">Namespace name regular expression.</param>
        protected static void DropNamespaces(Regex regex)
        {
            Ns.DropNamespaces(regex, DropDispositions.IfExists);
        }

        /// <summary>
        /// Drop tables from 'test' namespace.
        /// </summary>
        /// <param name = "regex">Table name regular expression.</param>
        protected static void DropTables(Regex regex)
        {
            Ns.DropTables(regex, DropDispositions.IfExists);
        }

        /// <summary>
        /// Creates and opens a table in the 'test' namespace, drops existing table. Using the specified type name as the table name.
        /// </summary>
        /// <param name = "type">Table name (type.Name).</param>
        /// <param name = "schema">Table xml schema.</param>
        /// <returns>Opened table.</returns>
        protected static ITable EnsureTable(Type type, string schema)
        {
            return EnsureTable(type.Name, schema);
        }

        /// <summary>
        /// Creates and opens a table in the 'test' namespace, drops existing table.
        /// </summary>
        /// <param name = "tableName">Table name.</param>
        /// <param name = "schema">Table xml schema.</param>
        /// <returns>Opened table.</returns>
        protected static ITable EnsureTable(string tableName, string schema)
        {
            return Ns.OpenTable(tableName, schema, OpenDispositions.CreateAlways);
        }

        /// <summary>
        /// Randomly shuffle the enumerable specified.
        /// </summary>
        /// <returns>
        /// The shuffled enumerable.
        /// </returns>
        protected static IEnumerable<T> Shuffle<T>(IEnumerable<T> enumerable)
        {
            if (enumerable == null)
            {
                throw new ArgumentNullException(nameof(enumerable));
            }

            var random = new Random((int)DateTime.Now.Ticks ^ enumerable.GetHashCode());
            return enumerable.Select(t => Tuple.Create(random.Next(), t)).OrderBy(pair => pair.Item1).Select(pair => pair.Item2).ToList();
        }
    }
}
