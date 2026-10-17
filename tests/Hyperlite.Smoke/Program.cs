// End-to-end smoke test for the Hyperlite managed chain: Context -> Client -> Namespace
// -> Table -> Mutator / Scanner, against the real Hyperlite.Native.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Hyperlite;

internal static class Program
{
    private static int failures;

    private static void Check(bool condition, string what)
    {
        if (!condition)
        {
            Console.WriteLine("FAIL " + what);
            ++failures;
        }
    }

    private static void Throws<T>(Action action, string what)
        where T : Exception
    {
        try
        {
            action();
            Console.WriteLine("FAIL " + what + " (no exception)");
            ++failures;
        }
        catch (T)
        {
        }
        catch (Exception e)
        {
            Console.WriteLine("FAIL " + what + " (got " + e.GetType().Name + ": " + e.Message + ")");
            ++failures;
        }
    }

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private static string S(byte[] v) => v == null ? null : Encoding.UTF8.GetString(v);

    /// <summary>
    ///     The database this runs against, relative to the working directory.
    /// </summary>
    private static readonly string[] Database = { "chain.db", "chain.db-journal" };

    private static int Main()
    {
        Discard();

        try
        {
            return Run();
        }
        finally
        {
            // leaving a database behind matters: test.cmd used to run this with a
            // framework's output directory as the working directory, and a chain.db
            // appeared among the binaries
            Discard();
        }
    }

    private static void Discard()
    {
        foreach (var stale in Database)
        {
            if (System.IO.File.Exists(stale))
            {
                System.IO.File.Delete(stale);
            }
        }
    }

    private static int Run()
    {

        //
        // connection string and property resolution
        //

        using (var context = Context.Create("Provider=SQLite; Uri=file://chain.db"))
        {
            Check((string)context.Properties["Provider"] == "SQLite", "Provider == SQLite");
            Check((string)context.Properties["Ht4n.Provider"] == "SQLite", "Ht4n.Provider alias");
            Check((string)context.Properties["Ht4n.SQLite.Filename"] == "chain.db", "filename from uri");

            // defaults must be present and typed, exactly as ht4n reported them
            Check((int)context.Properties["Ht4n.SQLite.CacheSizeMB"] == 64, "default CacheSizeMB 64");
            Check((int)context.Properties["Ht4n.SQLite.PageSizeKB"] == 4, "default PageSizeKB 4");
            Check(!(bool)context.Properties["Ht4n.SQLite.Index.Column"], "default Index.Column false");
            Check(!(bool)context.Properties["Ht4n.SQLite.Index.Timestamp"], "default Index.Timestamp false");

            Check(Context.HasContext(ContextKind.SQLite), "SQLite context available");

            //
            // namespaces
            //

            using (var client = context.CreateClient())
            {
                Check(!client.NamespaceExists("test"), "namespace absent up front");

                client.CreateNamespace("test");
                Check(client.NamespaceExists("test"), "namespace created");

                Throws<NamespaceExistsException>(
                    () => client.CreateNamespace("test"), "duplicate namespace throws NamespaceExistsException");

                client.CreateNamespace("test", CreateDispositions.CreateIfNotExist);
                client.CreateNamespace("test/sub/deep", CreateDispositions.CreateIntermediate);
                Check(client.NamespaceExists("test/sub"), "intermediate namespace created");

                Throws<NamespaceDoesNotExistException>(
                    () => client.OpenNamespace("nope"), "opening a missing namespace throws");

                using (var ns = client.OpenNamespace("test"))
                {
                    Check(ns.Name == "test", "namespace name");

                    //
                    // tables
                    //

                    const string Schema =
                        "<Schema><AccessGroup name=\"default\">" +
                        "<ColumnFamily><Name>a</Name></ColumnFamily>" +
                        "<ColumnFamily><Name>b</Name></ColumnFamily>" +
                        "</AccessGroup></Schema>";

                    Check(!ns.TableExists("t1"), "table absent up front");
                    ns.CreateTable("t1", Schema);
                    Check(ns.TableExists("t1"), "table created");

                    Throws<TableExistsException>(
                        () => ns.CreateTable("t1", Schema), "duplicate table throws TableExistsException");

                    ns.CreateTable("t1", Schema, CreateDispositions.CreateIfNotExist);

                    var withIds = ns.GetTableSchema("t1", true);
                    Check(withIds.Contains("id=\"1\""), "schema with ids assigns id 1");
                    Check(withIds.Contains("<Generation>"), "schema with ids has a generation");

                    var withoutIds = ns.GetTableSchema("t1");
                    Check(!withoutIds.Contains("id="), "schema without ids has no id");

                    ns.CreateTableLike("t2", "t1");
                    Check(ns.TableExists("t2"), "create-like produced t2");

                    Check(ns.Tables.Contains("t1") && ns.Tables.Contains("t2"), "table listing");
                    Check(ns.Namespaces.Contains("sub"), "namespace listing");

                    var listing = ns.GetListing(true);
                    Check(listing.Tables.Count == 2, "deep listing has 2 tables");
                    Check(listing.Namespaces.Count == 1, "deep listing has 1 namespace");
                    Check(listing.Namespaces[0].Name == "sub", "deep listing child name");
                    Check(listing.Namespaces[0].FullName == "test/sub", "deep listing child full name");
                    Check(listing.Namespaces[0].Namespaces.Count == 1, "deep listing grandchild");

                    //
                    // schema object model
                    //

                    using (var table = ns.OpenTable("t1"))
                    {
                        // the full path, as ht4c::SQLite::SQLiteTable::getName reported it
                        Check(table.Name == ns.Name + "/t1", "table name is the full path");

                        var model = table.GetTableSchema();
                        Check(model.AccessGroups.Count == 1, "schema model has one access group");
                        Check(model.AccessGroups[0].Name == "default", "access group name");
                        Check(model.AccessGroups[0].ColumnFamilies.Count == 2, "two column families");
                        Check(model.AccessGroups[0].ColumnFamilies[0].Name == "a", "column family a");

                        //
                        // mutate
                        //

                        using (var mutator = table.CreateMutator())
                        {
                            mutator.Set(new Key("r1", "a"), B("v-r1-a"));
                            mutator.Set(new Key("r1", "b", "q1"), B("v-r1-b-q1"));
                            mutator.Set(new Key("r2", "a"), B("v-r2-a"));

                            // batched
                            mutator.Set(new[]
                            {
                                new Cell(new Key("r3", "a"), B("v-r3-a")),
                                new Cell(new Key("r4", "a"), B("v-r4-a")),
                                new Cell(new Key("r5", "b", "q2"), B("v-r5-b-q2")),
                            });

                            // generated row key, written back into the key
                            var generated = new Key { ColumnFamily = "a" };
                            mutator.Set(generated, B("v-generated"));
                            Check(generated.Row != null && generated.Row.Length == 20,
                                "generated row key is 20 chars");

                            mutator.Flush();
                        }

                        //
                        // scan
                        //

                        Func<ScanSpec, int> count = spec =>
                        {
                            using (var scanner = table.CreateScanner(spec))
                            {
                                return scanner.Count();
                            }
                        };

                        Check(count(null) == 7, "full scan returns 7 cells");
                        Check(count(new ScanSpec().AddColumn("a")) == 5, "column a returns 5");
                        Check(count(new ScanSpec().AddColumn("b:q1")) == 1, "column b:q1 returns 1");
                        Check(count(new ScanSpec().AddColumn("b:^q")) == 2, "column b:^q returns 2");
                        Check(count(new ScanSpec("r1")) == 2, "row r1 returns 2");
                        Check(count(new ScanSpec(new RowInterval("r2", "r4"))) == 3, "interval r2..r4 returns 3");
                        Check(count(new ScanSpec(new RowInterval("r2", true, "r4", false))) == 2,
                            "interval r2..<r4 returns 2");

                        // Anchored on the whole row shape: a generated row key is random
                        // base85 and can perfectly well start with c, q or k, which would
                        // make a bare prefix match count one row too many about once in
                        // thirty runs.
                        // regex, through the managed callback
                        Check(count(new ScanSpec { RowRegex = "^r[12]$" }) == 3, "row regex returns 3");
                        Check(count(new ScanSpec { ValueRegex = "q1$" }) == 1, "value regex returns 1");

                        // scan and filter over a row set
                        var saf = new ScanSpec { ScanAndFilter = true };
                        saf.AddRow("r1").AddRow("r4");
                        Check(count(saf) == 3, "scan-and-filter r1,r4 returns 3");

                        // keys only
                        using (var scanner = table.CreateScanner(new ScanSpec { KeysOnly = true }))
                        {
                            var n = 0;
                            var anyValue = false;
                            foreach (var c in scanner)
                            {
                                anyValue |= c.ValueLength != 0;
                                ++n;
                            }

                            Check(n == 7, "keys-only returns 7 cells");
                            Check(!anyValue, "keys-only returns no values");
                        }

                        // max rows
                        using (var scanner = table.CreateScanner(new ScanSpec { MaxRows = 2 }))
                        {
                            var rows = new HashSet<string>();
                            foreach (var c in scanner)
                            {
                                rows.Add(c.Key.Row);
                            }

                            Check(rows.Count == 2, "MaxRows 2 yields 2 rows");
                        }

                        // Move into a caller owned cell, and the reuse variants
                        using (var scanner = table.CreateScanner(new ScanSpec("r1").AddColumn("b:q1")))
                        {
                            var cell = new Cell();
                            Check(scanner.Move(cell), "Move produced a cell");
                            Check(cell.Key.Row == "r1", "moved row");
                            Check(cell.Key.ColumnFamily == "b", "moved family");
                            Check(cell.Key.ColumnQualifier == "q1", "moved qualifier");
                            Check(S(cell.Value) == "v-r1-b-q1", "moved value");
                            Check(cell.Key.Timestamp != 0, "timestamp assigned");
                            Check(!scanner.Move(cell), "Move reports exhaustion");
                        }

                        using (var scanner = table.CreateScanner(new ScanSpec("r1").AddColumn("b:q1")))
                        {
                            var cell = new BufferedCell(64);
                            Check(scanner.Move(cell), "BufferedCell move");
                            Check(cell.ValueLength == "v-r1-b-q1".Length, "BufferedCell length");
                            Check(Encoding.UTF8.GetString(cell.Value, 0, cell.ValueLength) == "v-r1-b-q1",
                                "BufferedCell value");
                        }

                        using (var scanner = table.CreateScanner(new ScanSpec("r1").AddColumn("b:q1")))
                        {
                            var cell = new PooledCell();
                            Check(scanner.Move(cell), "PooledCell move");
                            Check(Encoding.UTF8.GetString(cell.Value, 0, cell.ValueLength) == "v-r1-b-q1",
                                "PooledCell value");
                            PooledCell.Return(cell.Value);
                        }

                        // zero copy value access
                        using (var scanner = table.CreateScanner(new ScanSpec("r1").AddColumn("b:q1")))
                        {
                            var seen = false;
                            var ok = scanner.Next((key, value, length) =>
                            {
                                seen = true;
                                return key.Row == "r1" && length == "v-r1-b-q1".Length && value != IntPtr.Zero;
                            });

                            Check(seen && ok, "Next(Func) saw the cell");
                        }

                        //
                        // chunked and queued mutators
                        //

                        using (var mutator = table.CreateMutator(MutatorSpec.CreateChunked()))
                        {
                            for (var i = 0; i < 50; ++i)
                            {
                                mutator.Set(new Key("c" + i.ToString("D3"), "a"), B("chunked-" + i));
                            }
                        }

                        Check(count(new ScanSpec { RowRegex = @"^c\d{3}$" }) == 50, "chunked mutator wrote 50");

                        using (var mutator = table.CreateMutator(MutatorSpec.CreateQueued()))
                        {
                            for (var i = 0; i < 50; ++i)
                            {
                                mutator.Set(new Key("q" + i.ToString("D3"), "a"), B("queued-" + i));
                            }

                            mutator.Flush();
                        }

                        Check(count(new ScanSpec { RowRegex = @"^q\d{3}$" }) == 50, "queued mutator wrote 50");

                        using (var mutator = table.CreateMutator(MutatorSpec.CreateChunkedQueued()))
                        {
                            for (var i = 0; i < 50; ++i)
                            {
                                mutator.Set(new Key("k" + i.ToString("D3"), "a"), B("both-" + i));
                            }
                        }

                        Check(count(new ScanSpec { RowRegex = @"^k\d{3}$" }) == 50, "chunked+queued mutator wrote 50");

                        //
                        // delete
                        //

                        using (var mutator = table.CreateMutator())
                        {
                            mutator.Delete("r1");
                            mutator.Flush();
                        }

                        Check(count(new ScanSpec("r1")) == 0, "row r1 gone");

                        using (var mutator = table.CreateMutator())
                        {
                            mutator.Delete(new Key("r5", "b", "q2"));
                            mutator.Flush();
                        }

                        Check(count(new ScanSpec("r5")) == 0, "cell r5/b:q2 gone");
                    }

                    //
                    // rename, drop
                    //

                    ns.RenameTable("t2", "t3");
                    Check(!ns.TableExists("t2") && ns.TableExists("t3"), "rename");

                    ns.DropTable("t3");
                    Check(!ns.TableExists("t3"), "drop");
                    Throws<HyperliteException>(() => ns.DropTable("t3"), "dropping a missing table throws");
                    ns.DropTable("t3", DropDispositions.IfExists);

                    ns.DropTables(new Regex("^t"), DropDispositions.IfExists);
                    Check(ns.Tables.Count == 0, "DropTables(regex) emptied the namespace");
                }

                Throws<HyperliteException>(
                    () => client.DropNamespace("test"), "dropping a non-empty namespace throws");

                client.DropNamespace("test", DropDispositions.Complete);
                Check(!client.NamespaceExists("test"), "namespace dropped");

                client.Optimize();
            }
        }

        //
        // reopen, proving persistence
        //

        using (var context = Context.Create("--Provider SQLite --Ht4n.SQLite.Filename chain.db"))
        {
            Check((string)context.Properties["Ht4n.SQLite.Filename"] == "chain.db",
                "explicit filename survives an unset uri");
            using (var client = context.CreateClient())
            {
                Check(!client.NamespaceExists("test"), "reopened database is empty");
            }
        }

        //
        // connection string forms
        //

        Throws<ArgumentException>(() => Context.Create("Provider="), "empty provider throws");
        Throws<ArgumentException>(() => Context.Create("Provider=Unknown"), "unknown provider throws");

        using (var context = Context.Create(
            "Provider=SQLite; Uri=file://chain.db; Ht4n.SQLite.PageSizeKB=16; " +
            "Ht4n.SQLite.CacheSizeMB=32; Ht4n.SQLite.Synchronous=yes"))
        {
            Check((int)context.Properties["Ht4n.SQLite.PageSizeKB"] == 16, "PageSizeKB 16");
            Check((int)context.Properties["Ht4n.SQLite.CacheSizeMB"] == 32, "CacheSizeMB 32");
            Check((bool)context.Properties["Ht4n.SQLite.Synchronous"], "Synchronous yes");
        }

        using (var context = Context.Create("Provider=SQLite; Uri=file:///c/Temp/chain-uri.db"))
        {
            Check((string)context.Properties["Ht4n.SQLite.Filename"] == "c:\\Temp\\chain-uri.db",
                "file:///c/... becomes c:\\...");
        }

        using (var context = Context.Create("Provider=SQLite; Uri=file:///c:/Temp/chain-uri.db"))
        {
            Check((string)context.Properties["Ht4n.SQLite.Filename"] == "c:\\Temp\\chain-uri.db",
                "file:///c:/... becomes c:\\...");
        }

        using (var context = Context.Create("Provider=SQLite; Uri=file:///c/My%20Documents/chain-uri.db"))
        {
            Check((string)context.Properties["Ht4n.SQLite.Filename"] == "c:\\My Documents\\chain-uri.db",
                "percent decoding");
        }

        using (var context = Context.Create(
            "Provider=SQLite; Uri=\"file:///c/My Documents/chain-uri.db\""))
        {
            Check((string)context.Properties["Ht4n.SQLite.Filename"] == "c:\\My Documents\\chain-uri.db",
                "quoted uri with spaces");
        }

        using (var context = Context.Create(
            "--Provider SQLite --Ht4n.SQLite.Filename \"My Documents/chain-uri.db\""))
        {
            Check((string)context.Properties["Ht4n.SQLite.Filename"] == "My Documents/chain-uri.db",
                "quoted filename in command line form");
        }

        using (var context = Context.Create("Provider=SQLite; Uri=file://%TEMP%/chain-uri.db"))
        {
            Check(((string)context.Properties["Ht4n.SQLite.Filename"]).EndsWith("\\chain-uri.db"),
                "environment expansion");
        }

        //
        // properties dictionary
        //

        using (var context = Context.Create(new Dictionary<string, object>
        {
            { Config.ProviderName, "SQLite" },
            { Config.SQLiteFilename, "chain.db" },
            { Config.SQLitePageSizeKB, 8 },
        }))
        {
            Check((int)context.Properties["Ht4n.SQLite.PageSizeKB"] == 8, "property dictionary honoured");
        }

        //
        // key round trip
        //

        foreach (var guid in new[]
        {
            Guid.Empty,
            Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"),
            Guid.Parse("F0000000-0000-0000-F000-000000000000"),
            Guid.NewGuid(),
        })
        {
            Check(Key.Decode(Key.Encode(guid)) == guid, "guid round trip " + guid);
        }

        for (var i = 0; i < 2000; ++i)
        {
            var key = Key.Generate();
            Check(key.Length == 20, "generated key length");
            Check(Key.Encode(Key.Decode(key)) == key, "generated key round trip");
        }

        Check(Key.Generate(typeof(Program))[0] == 'P', "typed key prefix first char");
        Check(Key.Generate(typeof(Program))[1] == 'm', "typed key prefix last char");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "PASS" : "FAILED (" + failures + " failures)");
        return failures == 0 ? 0 : 1;
    }
}
