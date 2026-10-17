namespace Hyperlite.Test
{
    using System.IO;
    using System.Linq;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     A row key, column family or qualifier with a NUL inside is refused, written or scanned.
    /// </summary>
    /// <remarks>
    ///     sqlite and the scan spec take these as text up to the first NUL. Accepted, a row
    ///     "a\0b" was written at its full length and looked up as "a", returning row a's cells;
    ///     a scan for "\0x" lost its row and returned the whole table; a family "a\0x" wrote into
    ///     family a; a predicate's qualifier was copied short and read past its end; and a regular
    ///     expression "ab\0c" was compiled as "ab".
    /// </remarks>
    [TestClass]
    public sealed class TestKeysWithNul
    {
        private const string Database = "keys-with-nul.db";

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
        public void AWriteWithANulIsRefused()
        {
            this.WithTable(table =>
            {
                using (var mutator = table.CreateMutator())
                {
                    Assert.ThrowsExactly<BadKeyException>(() => mutator.Set(new Key("a\0b", "a"), Encoding.UTF8.GetBytes("v")));
                    Assert.ThrowsExactly<BadKeyException>(() => mutator.Set(new Key("r", "a\0x"), Encoding.UTF8.GetBytes("v")));
                    Assert.ThrowsExactly<BadKeyException>(() => mutator.Set(new Key("r", "a", "q\0x"), Encoding.UTF8.GetBytes("v")));
                }

                // nothing of it was written
                using (var scanner = table.CreateScanner())
                {
                    Assert.IsFalse(scanner.Any());
                }
            });
        }

        [TestMethod]
        public void AScanWithANulIsRefused()
        {
            this.WithTable(table =>
            {
                Assert.ThrowsExactly<BadScanSpecException>(() => table.CreateScanner(new ScanSpec().AddRow("\0x")).Dispose());
                Assert.ThrowsExactly<BadScanSpecException>(() => table.CreateScanner(new ScanSpec().AddColumn("a:q\0x")).Dispose());
                Assert.ThrowsExactly<BadScanSpecException>(
                    () => table.CreateScanner(new ScanSpec().AddColumnPredicate(new ColumnPredicate("a", "q\0rest", MatchKind.QualifierExact))).Dispose());
                Assert.ThrowsExactly<BadScanSpecException>(() => table.CreateScanner(new ScanSpec { ValueRegex = "ab\0c" }).Dispose());
                Assert.ThrowsExactly<BadScanSpecException>(() => table.CreateScanner(new ScanSpec { RowRegex = "ab\0c" }).Dispose());
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

        private void WithTable(System.Action<ITable> body)
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
                        body(table);
                    }
                }
            }
        }
    }
}
