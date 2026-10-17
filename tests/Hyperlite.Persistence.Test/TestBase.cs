namespace Hyperlite.Persistence.Test
{
    using System;
    using System.Configuration;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;

    using Hyperlite;
    using Hyperlite.Persistence.Serialization;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     What the fixtures in this suite share: the connection string read from the app
    ///     config, the context, client and namespace built from it, and the helpers that
    ///     create and empty a table.
    /// </summary>
    [TestClass]
    public class TestBase
    {
        private static EntityManagerFactory emf;

        protected static string ConnectionString { get; private set; }

        protected static EntityManagerFactory Emf
        {
            get
            {
                if (emf == null)
                {
                    EntityManagerFactory.DefaultConfiguration.RootNamespace = NsName;
                    emf = EntityManagerFactory.CreateEntityManagerFactory(ConnectionString);
                }

                return emf;
            }
        }

        /// <summary>
        /// Gets the namespace name.
        /// </summary>
        protected static string NsName { get; private set; }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            if (emf != null)
            {
                emf.Clear();
                emf.Client.DropNamespace(NsName, DropDispositions.Complete);
                emf.Dispose();
            }
        }

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

            Hyperlite.Logging.Logfile = Assembly.GetAssembly(typeof(TestBase)).Location + ".log";
            Hyperlite.Logging.LogMessagePublished = message => Trace.WriteLine(message);

            const string Schema =
                "<Schema>" + "<AccessGroup name=\"default\">" + "<ColumnFamily>" + "<Name>e</Name>" + "<deleted>false</deleted>" + "</ColumnFamily>" + "<ColumnFamily>"
                + "<Name>a</Name>" + "<deleted>false</deleted>" + "</ColumnFamily>" + "<ColumnFamily>" + "<Name>b</Name>" + "<deleted>false</deleted>" + "</ColumnFamily>"
                + "<ColumnFamily>" + "<Name>c</Name>" + "<deleted>false</deleted>" + "</ColumnFamily>" + "</AccessGroup>" + "</Schema>";

            EnsureTable("EntityA", Schema);
            EnsureTable("EntityB", Schema);
            EnsureTable("TestEntityManager", Schema);
        }

        [TestInitialize]
        public void TestInitialize()
        {
            NativeMethods.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
            ClearNamespace();
        }

        protected static void ClearNamespace()
        {
            Emf.Clear();
            var ns = Emf.RootNamespace;
            foreach (var tableName in ns.GetListing(false).Tables)
            {
                using (var table = ns.OpenTable(tableName))
                {
                    Delete(table);
                }
            }

            System.Threading.Thread.Sleep(10);
        }

        /// <summary>
        /// Deletes all cells in the table specified.
        /// </summary>
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
        }

        /// <summary>
        /// Creates and opens a table in the 'test' namespace, drops existing table. Using the specified type name as the table name.
        /// </summary>
        /// <param name="schema">
        /// Table xml schema.
        /// </param>
        protected static void EnsureTable(string tableName, string schema)
        {
            Emf.RootNamespace.DropTable(tableName, DropDispositions.IfExists);
            Emf.RootNamespace.CreateTable(tableName, schema);
        }

        protected static void TestSerialization<T>(T t)
        {
            var b = Serializer.ToByteArray(t);
            Assert.IsNotNull(b);
            Assert.AreEqual(t, Deserializer.FromByteArray<T>(b));
        }
    }
}
