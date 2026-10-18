namespace Hyperlite.Test
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     What a flush promises: once it returns, what was written is committed - also while a
    ///     scan is open - and a mutator whose writes cannot go anywhere says so rather than hang.
    /// </summary>
    /// <remarks>
    ///     Committed is checked from a second connection, which sees nothing else. The file is
    ///     opened a second time through a hard link: environments are keyed by path, so the link
    ///     gets a connection of its own.
    /// </remarks>
    [TestClass]
    public sealed class TestTableMutatorFlush
    {
        private const string Database = "mutator-flush.db";

        private const string Link = "mutator-flush-link.db";

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
        public void AFlushCommitsWhileAScanIsOpen()
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
                        // two rows, so the scanner runs a statement per row and takes a read
                        // transaction for them
                        using (var scanner = table.CreateScanner(new ScanSpec().AddRow("x", "y")))
                        {
                            using (var mutator = table.CreateMutator())
                            {
                                mutator.Set(new Key("r", "a"), Encoding.UTF8.GetBytes("v"));
                            }

                            // the scan is still open, and what the flush reported has to be on disk
                            Assert.IsTrue(HardLink(Link, Database), "hard link");
                            using (var other = Context.Create("Provider=SQLite; Uri=file://" + Link))
                            using (var otherClient = other.CreateClient())
                            using (var otherNs = otherClient.OpenNamespace("test"))
                            using (var otherTable = otherNs.OpenTable("t"))
                            using (var otherScanner = otherTable.CreateScanner())
                            {
                                CollectionAssert.AreEqual(new[] { "r" }, otherScanner.Select(cell => cell.Key.Row).ToList());
                            }

                            Assert.IsFalse(scanner.Any());
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void AFailedBatchWritesNothing()
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
                        using (var mutator = table.CreateMutator())
                        {
                            // the last cell names a family the table does not have
                            var batch = new[]
                            {
                                new Cell(new Key("r1", "a"), Encoding.UTF8.GetBytes("v")),
                                new Cell(new Key("r2", "a"), Encoding.UTF8.GetBytes("v")),
                                new Cell(new Key("r3", "nosuchfamily"), Encoding.UTF8.GetBytes("v")),
                            };

                            try
                            {
                                mutator.Set(batch);
                                Assert.Fail("the batch did not fail");
                            }
                            catch (HyperliteException)
                            {
                            }
                        }

                        // reported as failed, so nothing of it was committed by the flush on dispose
                        using (var scanner = table.CreateScanner())
                        {
                            Assert.IsFalse(scanner.Any());
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void OptimizeCommitsWhatTheMutatorsHold()
        {
            using (var context = Context.Create("Provider=SQLite; Uri=file://" + Database))
            using (var client = context.CreateClient())
            {
                client.CreateNamespace("test", CreateDispositions.CreateIfNotExist);
                using (var ns = client.OpenNamespace("test"))
                {
                    ns.CreateTable("t", Schema, CreateDispositions.CreateIfNotExist);
                    using (var table = ns.OpenTable("t"))
                    using (var mutator = table.CreateMutator())
                    {
                        mutator.Set(new Key("r", "a"), Encoding.UTF8.GetBytes("v"));

                        // VACUUM cannot run inside the transaction the unflushed write is in
                        client.Optimize();

                        using (var scanner = table.CreateScanner())
                        {
                            CollectionAssert.AreEqual(new[] { "r" }, scanner.Select(cell => cell.Key.Row).ToList());
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void AQueuedMutatorWhoseWorkerFailedDoesNotHang()
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
                        var mutator = table.CreateMutator(new MutatorSpec(MutatorKind.Default) { Queued = true, Capacity = 1 });
                        var producer = Task.Run(() =>
                        {
                            // the worker fails on the first cell, a family the table does not have;
                            // the producer runs ahead of it and fills the queue, which nothing
                            // drains any more
                            mutator.Set(new Key("r", "nosuchfamily"), Encoding.UTF8.GetBytes("v"));
                            for (var i = 0; i < 1000; ++i)
                            {
                                mutator.Set(new Key("r" + i, "a"), Encoding.UTF8.GetBytes("v"));
                            }
                        });

                        Assert.IsTrue(SpinUntilDone(producer, TimeSpan.FromSeconds(30)), "the producer hung on a full queue");
                        Assert.IsTrue(producer.IsFaulted, "the worker's failure went unreported");
                        try
                        {
                            mutator.Dispose();
                            Assert.Fail("Dispose did not report the failure either");
                        }
                        catch (HyperliteException)
                        {
                        }
                    }
                }
            }
        }

        private static bool HardLink(string fileName, string existingFileName)
        {
            return Path.DirectorySeparatorChar == '\\'
                ? CreateHardLink(fileName, existingFileName, IntPtr.Zero)
                : link(existingFileName, fileName) == 0;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

        [DllImport("libc.so.6", SetLastError = true)]
        private static extern int link(string existingFileName, string fileName);

        private static bool SpinUntilDone(Task task, TimeSpan timeout)
        {
            try
            {
                return task.Wait(timeout);
            }
            catch (AggregateException)
            {
                return true;
            }
        }

        private static void Discard()
        {
            foreach (var stale in new[] { Link, Link + "-journal", Database, Database + "-journal" })
            {
                if (File.Exists(stale))
                {
                    File.Delete(stale);
                }
            }
        }
    }
}
