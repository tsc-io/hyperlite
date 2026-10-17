namespace Hyperlite.Test
{
    using System.IO;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     A name the store cannot keep as given is refused when it is given, not found wanting
    ///     later.
    /// </summary>
    /// <remarks>
    ///     A namespace or table name with a NUL inside was stored at its full length and listed up
    ///     to the NUL, so the listing named something that could not be opened. CreateTableLike
    ///     took a name no other call accepts, and the table could then be neither opened nor
    ///     dropped. A schema's &amp;#0; made a family name that was stored empty, after which the
    ///     table could not be opened at all - as did a tab in an access group's name, written back
    ///     raw and read as a space.
    /// </remarks>
    [TestClass]
    public sealed class TestNames
    {
        private const string Database = "names.db";

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
        public void ANameWithANulIsRefused()
        {
            this.WithNamespace((client, ns) =>
            {
                Assert.ThrowsExactly<SyntaxException>(() => client.CreateNamespace("a\0b"));
                Assert.ThrowsExactly<SyntaxException>(() => client.NamespaceExists("a\0b"));
                Assert.ThrowsExactly<SyntaxException>(() => ns.CreateTable("t\0x", Schema));
                Assert.ThrowsExactly<SyntaxException>(() => ns.TableExists("t\0x"));

                // nothing was created under the name cut short
                Assert.IsFalse(client.NamespaceExists("a"));
                Assert.IsFalse(ns.TableExists("t"));
            });
        }

        [TestMethod]
        public void CreateTableLikeRefusesANameNoOtherCallTakes()
        {
            this.WithNamespace((client, ns) =>
            {
                ns.CreateTable("t", Schema);
                Assert.ThrowsExactly<SyntaxException>(() => ns.CreateTableLike("^x", "t"));
                Assert.IsFalse(ns.TableExists("^x"));
            });
        }

        [TestMethod]
        public void ASchemaNamingACharacterXmlDoesNotAllowIsRefused()
        {
            this.WithNamespace((client, ns) =>
            {
                foreach (var reference in new[] { "&#0;", "&#xD800;", "&#x110000;" })
                {
                    var schema = "<Schema><AccessGroup name=\"default\"><ColumnFamily><Name>" + reference +
                                 "</Name></ColumnFamily></AccessGroup></Schema>";
                    Assert.ThrowsExactly<SchemaParseException>(() => ns.CreateTable("t", schema), reference);
                }

                Assert.IsFalse(ns.TableExists("t"));
            });
        }

        [TestMethod]
        public void AnAccessGroupNameKeepsItsWhiteSpace()
        {
            this.WithNamespace((client, ns) =>
            {
                // written raw, the tab read back as a space, and the table could not be opened
                ns.CreateTable(
                    "t",
                    "<Schema><AccessGroup name=\"a&#x9;b\"><ColumnFamily><Name>cf</Name></ColumnFamily></AccessGroup></Schema>");
                using (var table = ns.OpenTable("t"))
                {
                    Assert.AreEqual("a\tb", table.GetTableSchema().AccessGroups[0].Name);
                }

                // at either end it cannot survive the stored schema, so it is refused
                foreach (var name in new[] { " a", "a ", "&#x9;a" })
                {
                    var schema = "<Schema><AccessGroup name=\"" + name +
                                 "\"><ColumnFamily><Name>cf</Name></ColumnFamily></AccessGroup></Schema>";
                    Assert.ThrowsExactly<BadSchemaException>(() => ns.CreateTable("u", schema), name);
                }

                Assert.IsFalse(ns.TableExists("u"));
            });
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

        private void WithNamespace(System.Action<IClient, INamespace> body)
        {
            using (var context = Context.Create("Provider=SQLite; Uri=file://" + Database))
            using (var client = context.CreateClient())
            {
                client.CreateNamespace("test", CreateDispositions.CreateIfNotExist);
                using (var ns = client.OpenNamespace("test"))
                {
                    body(client, ns);
                }
            }
        }
    }
}
