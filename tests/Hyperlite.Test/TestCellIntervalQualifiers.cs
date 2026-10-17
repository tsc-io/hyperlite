namespace Hyperlite.Test
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Each cell interval of a scan starts at its own column qualifier.
    /// </summary>
    /// <remarks>
    ///     An interval starting at an empty qualifier kept the one the interval before it started at,
    ///     and lost the cells below it.
    /// </remarks>
    [TestClass]
    public sealed class TestCellIntervalQualifiers
    {
        private const string Database = "cell-interval-qualifiers.db";

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
        public void AnEmptyQualifierStartsAtTheBeginning()
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily><Name>b</Name></ColumnFamily>"));
                using (var table = ns.OpenTable("t"))
                {
                    using (var mutator = table.CreateMutator())
                    {
                        mutator.Set(new Key("r1", "a", "x"), Encoding.UTF8.GetBytes("a:x"));
                        mutator.Set(new Key("r2", "b", "m"), Encoding.UTF8.GetBytes("b:m"));
                    }

                    var spec = new ScanSpec()
                        .AddCellInterval(new CellInterval("r1", "a", "x", "r1", "a", "x"))
                        .AddCellInterval(new CellInterval("r2", "b", string.Empty, "r2", "b", "z"));
                    using (var scanner = table.CreateScanner(spec))
                    {
                        CollectionAssert.AreEqual(
                            new[] { "a:x", "b:m" }, scanner.Select(cell => Encoding.UTF8.GetString(cell.Value)).ToArray());
                    }
                }
            });
        }

        private static string Schema(string families)
        {
            return "<Schema><AccessGroup name=\"default\">" + families + "</AccessGroup></Schema>";
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

        private void WithClient(Action<IClient> body)
        {
            using (var context = Context.Create("Provider=SQLite; Uri=file://" + Database))
            using (var client = context.CreateClient())
            {
                body(client);
            }
        }

        private void WithNamespace(Action<INamespace> body)
        {
            this.WithClient(client =>
            {
                client.CreateNamespace("test", CreateDispositions.CreateIfNotExist);
                using (var ns = client.OpenNamespace("test"))
                {
                    body(ns);
                }
            });
        }
    }
}
