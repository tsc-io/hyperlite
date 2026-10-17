namespace Hyperlite.Test
{
    using System.IO;
    using System.Linq;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     A scanned cell carries the name of its own column family, whatever names came before.
    /// </summary>
    /// <remarks>
    ///     The family names of scanned cells go through a small per-thread cache. "Ã©"
    ///     four times is eight chars, and its char codes equal, one for one, the eight UTF-8
    ///     bytes of "é" four times - and the two land in the same slot. A cache comparing
    ///     chars against bytes handed the first name back for the second.
    /// </remarks>
    [TestClass]
    public sealed class TestColumnFamilyNames
    {
        private const string Database = "column-family-names.db";

        private static readonly string Mojibake = string.Concat(Enumerable.Repeat("Ã©", 4));

        private static readonly string Accented = new string('é', 4);

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
        public void ANonAsciiFamilyIsNeverMistakenForAnother()
        {
            var schema =
                "<Schema><AccessGroup name=\"default\">" +
                "<ColumnFamily><Name>" + Mojibake + "</Name></ColumnFamily>" +
                "<ColumnFamily><Name>" + Accented + "</Name></ColumnFamily>" +
                "</AccessGroup></Schema>";

            using (var context = Context.Create("Provider=SQLite; Uri=file://" + Database))
            using (var client = context.CreateClient())
            {
                client.CreateNamespace("test", CreateDispositions.CreateIfNotExist);
                using (var ns = client.OpenNamespace("test"))
                {
                    ns.CreateTable("t", schema, CreateDispositions.CreateIfNotExist);
                    using (var table = ns.OpenTable("t"))
                    {
                        using (var mutator = table.CreateMutator())
                        {
                            mutator.Set(new Key("r", Mojibake), Encoding.UTF8.GetBytes("first"));
                            mutator.Set(new Key("r", Accented), Encoding.UTF8.GetBytes("second"));
                        }

                        // one scan, the first family decoded right before the second is looked up
                        using (var scanner = table.CreateScanner())
                        {
                            var families = scanner.ToDictionary(cell => Encoding.UTF8.GetString(cell.Value), cell => cell.Key.ColumnFamily);
                            Assert.AreEqual(Mojibake, families["first"]);
                            Assert.AreEqual(Accented, families["second"]);
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
