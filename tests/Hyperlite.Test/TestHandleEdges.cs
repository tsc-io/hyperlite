namespace Hyperlite.Test
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     The edges of the handles: what they do when disposed, refused or misused.
    /// </summary>
    /// <remarks>
    ///     A scanner disposed from within the action Next hands its cell freed the cell under the
    ///     action. ToString threw once a table or namespace was disposed. A single cell larger than the
    ///     limit went to sqlite unchecked, a chunked delete of an empty row deleted a generated one, and
    ///     a regular expression that did not compile lost what the parser said about it.
    /// </remarks>
    [TestClass]
    public sealed class TestHandleEdges
    {
        private const string Database = "handle-edges.db";

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
        public void AScannerDisposedFromItsActionReleasesAfterIt()
        {
            this.WithTable(table =>
            {
                using (var mutator = table.CreateMutator())
                {
                    mutator.Set(new Key("r", "a"), Encoding.UTF8.GetBytes("value"));
                }

                var scanner = table.CreateScanner();
                string read = null;
                scanner.Next((key, value, length) =>
                {
                    scanner.Dispose();
                    var copy = new byte[length];
                    Marshal.Copy(value, copy, 0, length);
                    read = Encoding.UTF8.GetString(copy);
                    return true;
                });

                Assert.AreEqual("value", read);
                Assert.IsTrue(scanner.IsDisposed);
                Assert.ThrowsExactly<ObjectDisposedException>(() => scanner.Next(out _));
            });
        }

        [TestMethod]
        public void ToStringWorksOnceDisposed()
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>"));
                var table = ns.OpenTable("t");
                table.Dispose();
                StringAssert.Contains(table.ToString(), "disposed");

                var sub = ns.OpenNamespace("/");
                sub.Dispose();
                StringAssert.Contains(sub.ToString(), "disposed");
            });
        }

        [TestMethod]
        public void TheTraceSourceRefusesNull()
        {
            var refused = Assert.ThrowsExactly<ArgumentNullException>(() => Logging.TraceSource = null);
            Assert.AreEqual("value", refused.ParamName);
        }

        [TestMethod]
        public void ASingleCellOverTheLimitIsRefused()
        {
            this.WithTable(table =>
            {
                foreach (var spec in new[] { new MutatorSpec(), new MutatorSpec(MutatorKind.Chunked), MutatorSpec.CreateQueued() })
                {
                    using (var mutator = table.CreateMutator(spec))
                    {
                        // never read nor copied: the length is refused before the value is touched
                        var key = new Key(null, "a");
                        Assert.ThrowsExactly<ArgumentException>(() => mutator.Set(key, new IntPtr(1), int.MaxValue), spec.ToString());
                        Assert.IsNull(key.Row, spec.ToString());
                    }
                }
            });
        }

        [TestMethod]
        public void ARefusedBatchIsRefusedWhole()
        {
            this.WithTable(table =>
            {
                foreach (var spec in new[] { new MutatorSpec(), new MutatorSpec(MutatorKind.Chunked), MutatorSpec.CreateQueued(), MutatorSpec.CreateChunkedQueued() })
                {
                    using (var mutator = table.CreateMutator(spec))
                    {
                        var written = new Cell(new Key(null, "a"), Encoding.UTF8.GetBytes("v"));
                        var refused = new Cell(new Key(string.Empty, "a"), CellFlag.DeleteCell);
                        Assert.ThrowsExactly<BadKeyException>(() => mutator.Set(new[] { written, refused }), spec.ToString());

                        // neither taken nor given a row key
                        Assert.IsNull(written.Key.Row, spec.ToString());
                    }
                }

                using (var scanner = table.CreateScanner())
                {
                    Assert.AreEqual(0, scanner.Count());
                }
            });
        }

        [TestMethod]
        public void AReadOnlyDatabaseScansPastExpiredCells()
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name><TTL>3600</TTL></ColumnFamily>"));
                using (var table = ns.OpenTable("t"))
                using (var mutator = table.CreateMutator())
                {
                    mutator.Set(new Key("r", "a", "q", DateTime.UtcNow.AddHours(-2)), Encoding.UTF8.GetBytes("expired"));
                    mutator.Set(new Key("r", "a", "q", DateTime.UtcNow), Encoding.UTF8.GetBytes("live"));
                }
            });

            File.SetAttributes(Database, FileAttributes.ReadOnly);
            try
            {
                this.WithNamespace(ns =>
                {
                    using (var table = ns.OpenTable("t"))
                    using (var scanner = table.CreateScanner())
                    {
                        CollectionAssert.AreEqual(new[] { "live" }, scanner.Select(cell => Encoding.UTF8.GetString(cell.Value)).ToArray());
                    }
                });
            }
            finally
            {
                File.SetAttributes(Database, FileAttributes.Normal);
            }
        }

        [TestMethod]
        public void ADeleteOfAnEmptyRowIsRefusedByEveryMutator()
        {
            this.WithTable(table =>
            {
                using (var mutator = table.CreateMutator())
                {
                    mutator.Set(new Key("r", "a"), Encoding.UTF8.GetBytes("kept"));
                }

                foreach (var spec in new[] { new MutatorSpec(), new MutatorSpec(MutatorKind.Chunked), MutatorSpec.CreateQueued(), MutatorSpec.CreateChunkedQueued() })
                {
                    using (var mutator = table.CreateMutator(spec))
                    {
                        Assert.ThrowsExactly<BadKeyException>(() => mutator.Delete(new Key(string.Empty, "a")), spec.ToString());

                        // refused on its own, not with the cells around it
                        mutator.Set(new Key("s", "a"), Encoding.UTF8.GetBytes("kept"));
                    }
                }

                // r, and a version of s from each of the four mutators
                using (var scanner = table.CreateScanner())
                {
                    CollectionAssert.AreEqual(
                        new[] { "r", "s", "s", "s", "s" }, scanner.Select(cell => cell.Key.Row).ToArray());
                }
            });
        }

        [TestMethod]
        public void ARegularExpressionThatDoesNotCompileSaysWhy()
        {
            this.WithTable(table =>
            {
                var refused = Assert.ThrowsExactly<BadScanSpecException>(() => table.CreateScanner(new ScanSpec { RowRegex = "(" }).Dispose());
                Assert.IsInstanceOfType<ArgumentException>(refused.InnerException);
            });
        }

        private void WithTable(Action<ITable> body)
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>"));
                using (var table = ns.OpenTable("t"))
                {
                    body(table);
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
