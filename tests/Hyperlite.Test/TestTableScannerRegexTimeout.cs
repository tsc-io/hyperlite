namespace Hyperlite.Test
{
    using System.IO;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     A pattern that backtracks catastrophically fails the scan with a timeout, rather
    ///     than running forever or matching nothing.
    /// </summary>
    /// <remarks>
    ///     ht4c matched with RE2, which runs in linear time; .NET backtracks. Before the
    ///     timeout, the scan below held the database for as long as the match ran, and a
    ///     match that gave up would have been reported as no match - cells missing from the
    ///     result without a word.
    /// </remarks>
    [TestClass]
    public sealed class TestTableScannerRegexTimeout
    {
        private const string Database = "scanner-regex-timeout.db";

        private const string Schema =
            "<Schema><AccessGroup name=\"default\"><ColumnFamily><Name>a</Name></ColumnFamily></AccessGroup></Schema>";

        [TestInitialize]
        public void TestInitialize()
        {
            Discard();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            Discard();
        }

        [TestMethod]
        public void ACatastrophicPatternTimesOut()
        {
            using (var context = Context.Create("Provider=SQLite; Uri=file://" + Database))
            using (var client = context.CreateClient())
            {
                client.CreateNamespace("test", CreateDispositions.CreateIfNotExist);
                using (var ns = client.OpenNamespace("test"))
                {
                    ns.CreateTable("t", Schema, CreateDispositions.CreateIfNotExist);
                    using (var table = ns.OpenTable("t"))
                    {
                        using (var mutator = table.CreateMutator())
                        {
                            mutator.Set(new Key("r", "a"), Encoding.UTF8.GetBytes(new string('a', 60) + "!"));
                        }

                        var scanSpec = new ScanSpec { ValueRegex = "^(a|aa)+$" };
                        using (var scanner = table.CreateScanner(scanSpec))
                        {
                            Assert.ThrowsExactly<TimeoutException>(() => { scanner.Next(out var cell); });

                            // and stays failed: carrying on would skip the cell it failed on
                            Assert.ThrowsExactly<TimeoutException>(() => { scanner.Next(out var cell); });
                        }
                    }
                }
            }
        }

        private static void Discard()
        {
            foreach (var stale in new[] { Database, Database + "-journal" })
            {
                if (File.Exists(stale))
                {
                    File.Delete(stale);
                }
            }
        }
    }
}
