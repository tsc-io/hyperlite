namespace Hyperlite.Test
{
    using System.IO;
    using System.Linq;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     A column family's id belongs to it alone, from 1 to 255, and does not outlive it.
    /// </summary>
    /// <remarks>
    ///     An id is the family's key in every stored cell. Given explicitly it was taken as it
    ///     came: 300 wrote past the per-family tables and stored cells under 44, -1 made a schema
    ///     that could not be read back, and two families given the same id shared their cells.
    ///     An alter removing a family left its cells behind, for the next family given that id.
    /// </remarks>
    [TestClass]
    public sealed class TestColumnFamilyIds
    {
        private const string Database = "column-family-ids.db";

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
        public void AnIdOutsideTheRangeIsRefused()
        {
            this.WithNamespace(ns =>
            {
                foreach (var id in new[] { "300", "256", "-1" })
                {
                    Assert.ThrowsExactly<BadSchemaException>(() => ns.CreateTable("t", Schema("<ColumnFamily id=\"" + id + "\"><Name>a</Name></ColumnFamily>")), id);
                }

                Assert.IsFalse(ns.TableExists("t"));
            });
        }

        [TestMethod]
        public void TwoFamiliesSharingAnIdAreRefused()
        {
            this.WithNamespace(ns =>
            {
                Assert.ThrowsExactly<BadSchemaException>(
                    () => ns.CreateTable("t", Schema("<ColumnFamily id=\"1\"><Name>a</Name></ColumnFamily><ColumnFamily id=\"1\"><Name>b</Name></ColumnFamily>")));

                // and by an alter, where a keeps the id 1 it was given
                ns.CreateTable("u", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>"));
                Assert.ThrowsExactly<BadSchemaException>(
                    () => ns.AlterTable("u", Schema("<ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily id=\"1\"><Name>c</Name></ColumnFamily>")));
            });
        }

        [TestMethod]
        public void ARemovedFamilyTakesItsCellsWithIt()
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily><Name>b</Name></ColumnFamily>"));
                using (var table = ns.OpenTable("t"))
                using (var mutator = table.CreateMutator())
                {
                    mutator.Set(new Key("r", "a"), Encoding.UTF8.GetBytes("in a"));
                    mutator.Set(new Key("r", "b"), Encoding.UTF8.GetBytes("in b"));
                }

                // b goes, and c comes in with the next id - the one b had
                ns.AlterTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>"));
                ns.AlterTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily><Name>c</Name></ColumnFamily>"));

                using (var table = ns.OpenTable("t"))
                using (var scanner = table.CreateScanner())
                {
                    var cells = scanner.Select(cell => cell.Key.ColumnFamily + "=" + Encoding.UTF8.GetString(cell.Value)).ToList();
                    CollectionAssert.AreEqual(new[] { "a=in a" }, cells);
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

        private void WithNamespace(System.Action<INamespace> body)
        {
            using (var context = Context.Create("Provider=SQLite; Uri=file://" + Database))
            using (var client = context.CreateClient())
            {
                client.CreateNamespace("test", CreateDispositions.CreateIfNotExist);
                using (var ns = client.OpenNamespace("test"))
                {
                    body(ns);
                }
            }
        }
    }
}
