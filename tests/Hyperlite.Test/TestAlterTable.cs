namespace Hyperlite.Test
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     An alter replaces a table's schema under the handles open on it, and has to leave
    ///     both them and the stored cells consistent with the new one.
    /// </summary>
    /// <remarks>
    ///     The table handles dropped the schema an open mutator or scanner was still reading, so
    ///     using one after an alter read freed memory. Closing one handle of a table took the
    ///     others out of the alter's reach, and a handle opened before a rename failed the alter
    ///     looking the table up by its old name - as it went on reporting its old name, and the
    ///     schema of whatever table was created under it next. A family changing its time order
    ///     kept its stored timestamps, which such a family stores complemented. And the altered
    ///     table record took its unique rows and no cell revisions options from the session
    ///     altering it, rather than from the table.
    /// </remarks>
    [TestClass]
    public sealed class TestAlterTable
    {
        private const string Database = "alter-table.db";

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
        public void AMutatorOpenAcrossAnAlterFollowsIt()
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily><Name>b</Name></ColumnFamily>"));
                using (var table = ns.OpenTable("t"))
                {
                    using (var mutator = table.CreateMutator())
                    {
                        mutator.Set(new Key("r1", "a"), Value("1"));

                        // b goes, and c comes in with the id b had
                        ns.AlterTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily><Name>c</Name></ColumnFamily>"));

                        Assert.ThrowsExactly<BadKeyException>(() => mutator.Set(new Key("r2", "b"), Value("2")));
                        mutator.Set(new Key("r3", "c"), Value("3"));
                        mutator.Set(new Key("r4", "a"), Value("4"));
                    }

                    CollectionAssert.AreEqual(new[] { "r1 a=1", "r3 c=3", "r4 a=4" }, Scan(table));
                }
            });
        }

        [TestMethod]
        public void AMutatorFollowsAnAlterWhenAnotherHandleOfTheTableWasClosed()
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>"));
                using (var table = ns.OpenTable("t"))
                {
                    ns.OpenTable("t").Dispose();
                    using (var mutator = table.CreateMutator())
                    {
                        ns.AlterTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily><Name>c</Name></ColumnFamily>"));
                        mutator.Set(new Key("r", "c"), Value("in c"));
                    }

                    CollectionAssert.AreEqual(new[] { "r c=in c" }, Scan(table));
                }
            });
        }

        [TestMethod]
        public void AHandleOpenedBeforeARenameFollowsTheAlter()
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("a", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>"));
                using (var table = ns.OpenTable("a"))
                {
                    ns.RenameTable("a", "b");
                    ns.AlterTable("b", Schema("<ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily><Name>c</Name></ColumnFamily>"));

                    using (var mutator = table.CreateMutator())
                    {
                        mutator.Set(new Key("r", "c"), Value("in c"));
                    }
                }

                using (var table = ns.OpenTable("b"))
                {
                    CollectionAssert.AreEqual(new[] { "r c=in c" }, Scan(table));
                }
            });
        }

        [TestMethod]
        public void AHandleOpenedBeforeARenameTakesTheNewName()
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("a", Schema("<ColumnFamily><Name>x</Name></ColumnFamily>"));
                using (var table = ns.OpenTable("a"))
                {
                    ns.RenameTable("a", "b");
                    ns.CreateTable("a", Schema("<ColumnFamily><Name>y</Name></ColumnFamily>"));

                    using (var renamed = ns.OpenTable("b"))
                    {
                        Assert.AreEqual(renamed.Name, table.Name);
                    }

                    StringAssert.Contains(table.Schema, "<Name>x</Name>");
                    Assert.IsFalse(table.Schema.Contains("<Name>y</Name>"));
                }
            });
        }

        [TestMethod]
        public void AScannerOpenAcrossAnAlterFails()
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
                            mutator.Set(new Key(row, "a"), Value(row));
                        }
                    }

                    using (var scanner = table.CreateScanner())
                    {
                        Assert.IsTrue(scanner.Next(out _));

                        ns.AlterTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily><Name>c</Name></ColumnFamily>"));

                        Assert.ThrowsExactly<RangeServerException>(() => scanner.Next(out _));
                        Assert.ThrowsExactly<RangeServerException>(() => scanner.Next(out _));
                    }

                    // a scanner opened after the alter reads the table as it is
                    CollectionAssert.AreEqual(new[] { "r1 a=r1", "r2 a=r2", "r3 a=r3" }, Scan(table));
                }
            });
        }

        [TestMethod]
        public void AScanThatHasEndedStaysEndedAcrossAnAlter()
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>"));
                using (var table = ns.OpenTable("t"))
                {
                    using (var mutator = table.CreateMutator())
                    {
                        mutator.Set(new Key("r", "a"), Value("in a"));
                    }

                    using (var scanner = table.CreateScanner())
                    {
                        Assert.IsTrue(scanner.Next(out _));
                        Assert.IsFalse(scanner.Next(out _));

                        ns.AlterTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily><Name>c</Name></ColumnFamily>"));

                        Assert.IsFalse(scanner.Next(out _));
                    }
                }
            });
        }

        [TestMethod]
        public void ChangingTheTimeOrderKeepsTheTimestamps()
        {
            const string Ascending = "<ColumnFamily><Name>a</Name><TimeOrder>ASC</TimeOrder></ColumnFamily>";
            const string Descending = "<ColumnFamily><Name>a</Name><TimeOrder>DESC</TimeOrder></ColumnFamily>";

            this.WithNamespace(ns =>
            {
                // what each time order reads, from tables created with it
                var ascending = Seeded(ns, "asc", Ascending, 1000, 2000, 3000);
                var descending = Seeded(ns, "desc", Descending, 1000, 2000, 3000);
                CollectionAssert.AreNotEqual(ascending, descending);

                ns.CreateTable("t", Schema(Ascending));
                using (var table = ns.OpenTable("t"))
                {
                    using (var mutator = table.CreateMutator())
                    {
                        mutator.Set(new Key("r", "a") { Timestamp = 1000 }, Value("1000"));
                        mutator.Set(new Key("r", "a") { Timestamp = 2000 }, Value("2000"));

                        ns.AlterTable("t", Schema(Descending));

                        // and the mutator open across the alter writes the new way
                        mutator.Set(new Key("r", "a") { Timestamp = 3000 }, Value("3000"));
                    }

                    CollectionAssert.AreEqual(descending, ScanWithTimestamps(table));

                    ns.AlterTable("t", Schema(Ascending));
                    CollectionAssert.AreEqual(ascending, ScanWithTimestamps(table));
                }
            });
        }

        [TestMethod]
        public void AnAlterKeepsTheTableOptions()
        {
            var options = new Dictionary<string, object>
            {
                { Config.SQLiteUniqueRows, true },
                { Config.SQLiteNoCellRevisions, true },
            };
            const string Altered = "<ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily><Name>b</Name></ColumnFamily>";

            // a table created without the options, altered by a session that has them
            this.WithNamespace(ns => ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>")));
            this.WithNamespace(options, ns => ns.AlterTable("t", Schema(Altered)));
            CollectionAssert.AreEqual(Array.Empty<string>(), StoredOptions());

            // and the other way round, which also shows the session's options take effect
            Discard();
            this.WithNamespace(options, ns => ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>")));
            CollectionAssert.AreEqual(new[] { "{UR}", "{NCR}" }, StoredOptions());
            this.WithNamespace(ns => ns.AlterTable("t", Schema(Altered)));
            CollectionAssert.AreEqual(new[] { "{UR}", "{NCR}" }, StoredOptions());
        }

        private static string Schema(string families)
        {
            return "<Schema><AccessGroup name=\"default\">" + families + "</AccessGroup></Schema>";
        }

        /// <summary>
        /// The options the table record carries, read from the file once a vacuum has taken out
        /// the records it replaced: the record is the only place they are written.
        /// </summary>
        private static string[] StoredOptions()
        {
            using (var context = Context.Create("Provider=SQLite; Uri=file://" + Database))
            using (var client = context.CreateClient())
            {
                client.Optimize();
            }

            var stored = Encoding.ASCII.GetString(File.ReadAllBytes(Database));
            return new[] { "{UR}", "{NCR}" }.Where(stored.Contains).ToArray();
        }

        private static byte[] Value(string value)
        {
            return Encoding.UTF8.GetBytes(value);
        }

        private static List<string> Seeded(INamespace ns, string name, string family, params ulong[] timestamps)
        {
            ns.CreateTable(name, Schema(family));
            using (var table = ns.OpenTable(name))
            {
                using (var mutator = table.CreateMutator())
                {
                    foreach (var timestamp in timestamps)
                    {
                        mutator.Set(new Key("r", "a") { Timestamp = timestamp }, Value(timestamp.ToString()));
                    }
                }

                return ScanWithTimestamps(table);
            }
        }

        private static List<string> Scan(ITable table)
        {
            using (var scanner = table.CreateScanner())
            {
                return scanner.Select(cell => cell.Key.Row + " " + cell.Key.ColumnFamily + "=" + Encoding.UTF8.GetString(cell.Value)).ToList();
            }
        }

        private static List<string> ScanWithTimestamps(ITable table)
        {
            using (var scanner = table.CreateScanner())
            {
                return scanner.Select(cell => cell.Key.Timestamp + "=" + Encoding.UTF8.GetString(cell.Value)).ToList();
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

        private void WithNamespace(Action<INamespace> body)
        {
            this.WithNamespace(new Dictionary<string, object>(), body);
        }

        private void WithNamespace(IDictionary<string, object> properties, Action<INamespace> body)
        {
            using (var context = Context.Create("Provider=SQLite; Uri=file://" + Database, properties))
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
