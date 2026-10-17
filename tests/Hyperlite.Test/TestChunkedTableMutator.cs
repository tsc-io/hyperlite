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
    ///     What a chunked mutator holds back: every cell keeps its place in the chunk, and none is
    ///     accepted once the last chunk has been written.
    /// </summary>
    /// <remarks>
    ///     A database of its own, so that the order of revisions on one key is all there is.
    /// </remarks>
    [TestClass]
    public sealed class TestChunkedTableMutator
    {
        private const string Database = "chunked-mutator.db";

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
        public void AnUnmanagedValueKeepsItsPlaceInTheChunk()
        {
            this.WithTable(table =>
            {
                var key = new Key("r", "a");
                using (var mutator = table.CreateMutator(MutatorSpec.CreateChunked()))
                {
                    mutator.Set(key, Encoding.UTF8.GetBytes("first"));

                    // written straight through, it would land ahead of the cell before it
                    var second = Encoding.UTF8.GetBytes("second");
                    var unmanaged = Marshal.AllocHGlobal(second.Length);
                    try
                    {
                        Marshal.Copy(second, 0, unmanaged, second.Length);
                        mutator.Set(key, unmanaged, second.Length);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(unmanaged);
                    }
                }

                using (var scanner = table.CreateScanner(new ScanSpec { MaxVersions = 1 }))
                {
                    var values = scanner.Select(cell => Encoding.UTF8.GetString(cell.Value)).ToList();
                    CollectionAssert.AreEqual(new[] { "second" }, values);
                }
            });
        }

        [TestMethod]
        public void ADisposedChunkedMutatorRefusesCells()
        {
            this.WithTable(table =>
            {
                var mutator = table.CreateMutator(MutatorSpec.CreateChunked());
                mutator.Dispose();

                // accepted, it would sit in a chunk that is never written
                Assert.ThrowsExactly<ObjectDisposedException>(() => mutator.Set(new Key("r", "a"), Encoding.UTF8.GetBytes("v")));
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

        private void WithTable(Action<ITable> body)
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
