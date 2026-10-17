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
    ///     A delete takes the cells at or older than its timestamp, and an expired cell goes, in
    ///     a column family of either time order.
    /// </summary>
    /// <remarks>
    ///     A time-order-asc family stores the complement of its timestamps, and the deletes only
    ///     ever compared the way that order needs. In a time-order-desc family a delete of a
    ///     cell or a family took the newer cells and left the older ones, a delete of a row took
    ///     all of them, and a scan passing an expired cell deleted the cells that had not expired.
    ///     A delete at the greatest timestamp took nothing, as one was added to it, and a scan left
    ///     the newest of the expired cells it passed in the table.
    /// </remarks>
    [TestClass]
    public sealed class TestTimeOrderDeletes
    {
        private const string Database = "time-order-deletes.db";

        private static readonly string[] Orders = { "ASC", "DESC" };

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
        public void ACellDeleteTakesTheOlderVersions()
        {
            foreach (var order in Orders)
            {
                this.WithTable(Family("a", order), table =>
                {
                    Seed(table, "a");
                    using (var mutator = table.CreateMutator())
                    {
                        mutator.Delete(new Key("r", "a", "q") { Timestamp = 2000 });
                    }

                    CollectionAssert.AreEqual(new[] { "a 3000" }, Scan(table), order);
                });
            }
        }

        [TestMethod]
        public void AFamilyDeleteTakesTheOlderVersions()
        {
            foreach (var order in Orders)
            {
                this.WithTable(Family("a", order), table =>
                {
                    Seed(table, "a");
                    using (var mutator = table.CreateMutator())
                    {
                        mutator.Delete(new Key("r", "a") { Timestamp = 2000 });
                    }

                    CollectionAssert.AreEqual(new[] { "a 3000" }, Scan(table), order);
                });
            }
        }

        [TestMethod]
        public void ARowDeleteTakesTheOlderVersionsOfEitherOrder()
        {
            this.WithTable(Family("a", "ASC") + Family("d", "DESC"), table =>
            {
                Seed(table, "a");
                Seed(table, "d");
                using (var mutator = table.CreateMutator())
                {
                    mutator.Delete(new Key("r") { Timestamp = 2000 });
                }

                CollectionAssert.AreEqual(new[] { "a 3000", "d 3000" }, Scan(table));
            });
        }

        [TestMethod]
        public void ADeleteAtTheGreatestTimestampTakesEverything()
        {
            foreach (var order in Orders)
            {
                this.WithTable(Family("a", order), table =>
                {
                    Seed(table, "a");
                    using (var mutator = table.CreateMutator())
                    {
                        mutator.Delete(new Key("r", "a", "q") { Timestamp = long.MaxValue });
                    }

                    CollectionAssert.AreEqual(Array.Empty<string>(), Scan(table), order);
                });
            }
        }

        [TestMethod]
        public void AnExpiredCellIsRemovedNotOnlyHidden()
        {
            foreach (var order in Orders)
            {
                this.WithNamespace(ns =>
                {
                    ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name><TimeOrder>" + order + "</TimeOrder><TTL>3600</TTL></ColumnFamily>"));
                    using (var table = ns.OpenTable("t"))
                    {
                        var now = DateTime.UtcNow;
                        using (var mutator = table.CreateMutator())
                        {
                            mutator.Set(new Key("r", "a", "q", now.AddHours(-3)), Encoding.UTF8.GetBytes("older"));
                            mutator.Set(new Key("r", "a", "q", now.AddHours(-2)), Encoding.UTF8.GetBytes("newer"));
                        }

                        CollectionAssert.AreEqual(Array.Empty<string>(), Values(table), order);

                        // without the ttl a cell left in the table would show again
                        ns.AlterTable("t", Schema(Family("a", order)));
                        CollectionAssert.AreEqual(Array.Empty<string>(), Values(table), order);
                    }
                });
            }
        }

        [TestMethod]
        public void AnExpiredCellLeavesTheOthers()
        {
            foreach (var order in Orders)
            {
                this.WithTable("<ColumnFamily><Name>a</Name><TimeOrder>" + order + "</TimeOrder><TTL>3600</TTL></ColumnFamily>", table =>
                {
                    var now = DateTime.UtcNow;
                    using (var mutator = table.CreateMutator())
                    {
                        mutator.Set(new Key("r", "a", "q", now.AddHours(-2)), Encoding.UTF8.GetBytes("expired"));
                        mutator.Set(new Key("r", "a", "q", now.AddMinutes(-1)), Encoding.UTF8.GetBytes("older"));
                        mutator.Set(new Key("r", "a", "q", now), Encoding.UTF8.GetBytes("newer"));
                    }

                    // the first scan passes the expired cell, the second reads what it left
                    var expected = order == "ASC" ? new[] { "newer", "older" } : new[] { "older", "newer" };
                    CollectionAssert.AreEqual(expected, Values(table), order);
                    CollectionAssert.AreEqual(expected, Values(table), order);
                });
            }
        }

        private static string Schema(string families)
        {
            return "<Schema><AccessGroup name=\"default\">" + families + "</AccessGroup></Schema>";
        }

        private static string Family(string name, string order)
        {
            return "<ColumnFamily><Name>" + name + "</Name><TimeOrder>" + order + "</TimeOrder></ColumnFamily>";
        }

        private static void Seed(ITable table, string family)
        {
            using (var mutator = table.CreateMutator())
            {
                foreach (var timestamp in new ulong[] { 1000, 2000, 3000 })
                {
                    mutator.Set(new Key("r", family, "q") { Timestamp = timestamp }, Encoding.UTF8.GetBytes(timestamp.ToString()));
                }
            }
        }

        private static List<string> Scan(ITable table)
        {
            using (var scanner = table.CreateScanner())
            {
                return scanner.Select(cell => cell.Key.ColumnFamily + " " + cell.Key.Timestamp).ToList();
            }
        }

        private static List<string> Values(ITable table)
        {
            using (var scanner = table.CreateScanner())
            {
                return scanner.Select(cell => Encoding.UTF8.GetString(cell.Value)).ToList();
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

        private void WithTable(string families, Action<ITable> body)
        {
            this.WithNamespace(ns =>
            {
                ns.CreateTable("t", Schema(families));
                using (var table = ns.OpenTable("t"))
                {
                    body(table);
                }
            });
        }

        private void WithNamespace(Action<INamespace> body)
        {
            Discard();
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
