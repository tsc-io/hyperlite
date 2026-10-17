namespace Hyperlite.Test
{
    using System;
    using System.IO;
    using System.Linq;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     A namespace lists, and refuses to drop with, what it holds, whatever its siblings are called.
    /// </summary>
    /// <remarks>
    ///     A sub-tree was walked from the first key after the namespace's own, and ended at the first
    ///     key outside it. A sibling continuing the name with a character sorting below '/' - "app-test"
    ///     beside "app" - came first, so the namespace listed nothing, and dropped as empty with its
    ///     tables still in it. A qualified table name skipped the check that its namespace exists,
    ///     so "missing/t" was stored where no listing reached it, and its namespace could never be
    ///     dropped; "a//t" was listed as "t", which could not be opened.
    /// </remarks>
    [TestClass]
    public sealed class TestNamespaceSiblings
    {
        private const string Database = "namespace-siblings.db";

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
        public void ASiblingDoesNotHideTheTables()
        {
            this.WithNamespace(ns =>
            {
                ns.CreateNamespace("app");
                ns.CreateNamespace("app-test");
                using (var app = ns.OpenNamespace("app"))
                {
                    app.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>"));
                    CollectionAssert.AreEqual(new[] { "t" }, app.Tables.ToArray());
                }

                Assert.ThrowsExactly<HyperspaceException>(() => ns.DropNamespace("app"));
                using (var app = ns.OpenNamespace("app"))
                {
                    Assert.IsTrue(app.TableExists("t"));
                }
            });
        }

        [TestMethod]
        public void AQualifiedTableNameNeedsItsNamespace()
        {
            this.WithNamespace(ns =>
            {
                var schema = Schema("<ColumnFamily><Name>a</Name></ColumnFamily>");
                Assert.ThrowsExactly<NamespaceDoesNotExistException>(() => ns.CreateTable("missing/t", schema));
                Assert.ThrowsExactly<SyntaxException>(() => ns.CreateTable("a//t", schema));

                ns.CreateNamespace("sub");
                ns.CreateTable("sub/t", schema);
                using (var sub = ns.OpenNamespace("sub"))
                {
                    CollectionAssert.AreEqual(new[] { "t" }, sub.Tables.ToArray());
                }

                Assert.ThrowsExactly<NamespaceDoesNotExistException>(() => ns.RenameTable("sub/t", "missing/t"));

                ns.DropNamespace("sub", DropDispositions.Complete);
                Assert.IsFalse(ns.NamespaceExists("sub"));
            });
        }

        [TestMethod]
        public void CreateIntermediateChecksTheNameFirst()
        {
            this.WithNamespace(ns =>
            {
                var schema = Schema("<ColumnFamily><Name>a</Name></ColumnFamily>");
                const CreateDispositions Dispo = CreateDispositions.CreateIntermediate | CreateDispositions.CreateIfNotExist;

                // refused before the namespace it names is created
                Assert.ThrowsExactly<SyntaxException>(() => ns.CreateTable("a//t", schema, Dispo));
                Assert.IsFalse(ns.NamespaceExists("a"));

                // and trimmed before it is looked for, so the second call finds the first's table
                ns.CreateTable("/sub/t", schema, Dispo);
                ns.CreateTable("/sub/t", schema, Dispo);
                Assert.IsTrue(ns.TableExists("sub/t"));

                // and so does CreateTableLike
                ns.CreateTableLike("/like", "sub/t", CreateDispositions.CreateIfNotExist);
                ns.CreateTableLike("/like", "sub/t", CreateDispositions.CreateIfNotExist);
                Assert.IsTrue(ns.TableExists("like"));

                // and OpenTable, which creates one when asked to
                ns.OpenTable("/opened", schema, OpenDispositions.OpenAlways).Dispose();
                ns.OpenTable("/opened", schema, OpenDispositions.OpenAlways).Dispose();
                Assert.IsTrue(ns.TableExists("opened"));
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
