namespace Hyperlite.Test
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     A drop that fails leaves the table, and the handles open on it, as they were.
    /// </summary>
    /// <remarks>
    ///     The handles were disposed before the table was dropped. sqlite refuses the drop while
    ///     another statement is reading, and then the table was still there but every handle on it
    ///     reported itself disposed.
    /// </remarks>
    [TestClass]
    public sealed class TestFailedDrop
    {
        private const string Database = "failed-drop.db";

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
        public void TheHandlesSurviveADropThatFails()
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>"));
                using (var table = ns.OpenTable("t"))
                {
                    using (var mutator = table.CreateMutator())
                    {
                        foreach (var row in new[] { "r1", "r2", "r3" })
                        {
                            mutator.Set(new Key(row, "a"), Encoding.UTF8.GetBytes(row));
                        }
                    }

                    using (var scanner = table.CreateScanner())
                    {
                        Assert.IsTrue(scanner.Next(out _));
                        Assert.ThrowsExactly<HyperliteException>(() => ns.DropTable("t"));
                    }

                    Assert.IsTrue(ns.TableExists("t"));
                    using (var scanner = table.CreateScanner())
                    {
                        Assert.AreEqual(3, scanner.Count());
                    }
                }

                ns.DropTable("t");
                Assert.IsFalse(ns.TableExists("t"));
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
