namespace Hyperlite.Test
{
    using System.IO;
    using System.Linq;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Disposing a mutator flushes it, and a flush that fails says so.
    /// </summary>
    /// <remarks>
    ///     The commit is made to fail by holding sqlite's pending lock byte on the database file
    ///     from outside: a commit needs that byte, so it waits out the busy timeout and fails
    ///     with SQLITE_BUSY - what another process reading the file would cause. A database of
    ///     its own, because the environment the failed commit leaves behind is not one for
    ///     other fixtures to share.
    /// </remarks>
    [TestClass]
    public sealed class TestTableMutatorDispose
    {
        private const string Database = "mutator-dispose.db";

        // sqlite's PENDING_BYTE, see its os_win.c
        private const long PendingByte = 0x40000000;

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
        public void AFlushThatFailsOnDisposeThrows()
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
                        var mutator = table.CreateMutator();
                        mutator.Set(new Key("r", "a"), Encoding.UTF8.GetBytes("v"));

                        // Windows only, as Hyperlite is
#pragma warning disable CA1416
                        using (var file = new FileStream(Database, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                        {
                            file.Lock(PendingByte, 1);
                            try
                            {
                                mutator.Dispose();
                                Assert.Fail("the failed flush went unreported");
                            }
                            catch (HyperliteException)
                            {
                            }
                            finally
                            {
                                file.Unlock(PendingByte, 1);
                            }
                        }
#pragma warning restore CA1416

                        // released all the same, and a second dispose does nothing
                        Assert.IsTrue(mutator.IsDisposed);
                        mutator.Dispose();

                        // what was reported as failed stays failed - it is not committed later
                        // by someone else's flush - and the database takes writes again
                        using (var next = table.CreateMutator())
                        {
                            next.Set(new Key("s", "a"), Encoding.UTF8.GetBytes("w"));
                        }

                        using (var scanner = table.CreateScanner())
                        {
                            var rows = scanner.Select(cell => cell.Key.Row).ToList();
                            CollectionAssert.AreEqual(new[] { "s" }, rows);
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
