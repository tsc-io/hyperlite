namespace Hyperlite.Test
{
    using System;
    using System.Configuration;
    using System.IO;
    using System.Threading;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Creating a context: from a connection string, from a configuration file,
    ///     several at once, and again after the first was disposed.
    /// </summary>
    [TestClass]
    public sealed class TestContextCreation
    {
        private const ushort DefaultHyperspacePort = 15861;

        private const ushort DefaultThriftPort = 15867;

        private static string connectionString;

        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            connectionString = ConfigurationManager.AppSettings["ConnectionString"].Trim();
        }

        [TestMethod]
        public void CreateFromConfigurationFile()
        {
            // conf\TestConfiguration.cfg carries three kinds of option, and each takes a
            // different path through PropertyResolver: the provider name, one option the
            // registry knows the type of, and three it does not know at all. Found beside the
            // assembly, which is not the working directory under test.cmd.
            var configuration = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "conf", "TestConfiguration.cfg");
            using (var context = Context.Create("--config \"" + configuration + "\""))
            {
                Assert.AreEqual("SQLite", context.Properties["Hyperlite.Provider"]);
                Assert.AreEqual("SQLite", context.Properties["Provider"]);

                // the pre-rename spelling answers too, from a configuration file as
                // much as from a connection string
                Assert.AreEqual("SQLite", context.Properties["Ht4n.Provider"]);

                Assert.AreEqual("config-test.db", context.Properties["Hyperlite.SQLite.Filename"]);

                // registered, so it arrives typed rather than as text
                Assert.AreEqual(8, context.Properties["Hyperlite.SQLite.PageSizeKB"]);

                // unregistered, so carried through untouched
                Assert.AreEqual("Unknown", context.Properties["X.Y.String"]);
                Assert.AreEqual("Yes", context.Properties["X.Y.Bool"]);
                Assert.AreEqual("32", context.Properties["X.Y.Int"]);
            }
        }

        [TestMethod]
        public void CreateFromConnectionString()
        {
            try
            {
                using (Context.Create("Provider="))
                {
                    Assert.Fail();
                }
            }
            catch (ArgumentException)
            {
            }

            try
            {
                using (Context.Create("Provider=Unknown"))
                {
                    Assert.Fail();
                }
            }
            catch (ArgumentException)
            {
            }

            if (Context.HasContext(ContextKind.SQLite))
            {
                using (var context = Context.Create("Provider=SQLite; Uri=file://test.db"))
                {
                    Assert.AreEqual("SQLite", context.Properties["Provider"]);
                    Assert.AreEqual("test.db", context.Properties["Ht4n.SQLite.Filename"]);
                }

                // drive letters and backslashes on Windows, the path as it is elsewhere
                var windows = Path.DirectorySeparatorChar == '\\';

                using (var context = Context.Create("Provider=SQLite; Uri=file://Temp/test.db"))
                {
                    Assert.AreEqual("SQLite", context.Properties["Provider"]);
                    Assert.AreEqual(windows ? "Temp\\test.db" : "Temp/test.db", context.Properties["Ht4n.SQLite.Filename"]);
                }

                if (windows)
                {
                    using (var context = Context.Create("Provider=SQLite; Uri=file:///c/Temp/test.db"))
                    {
                        Assert.AreEqual("SQLite", context.Properties["Provider"]);
                        Assert.AreEqual("c:\\Temp\\test.db", context.Properties["Ht4n.SQLite.Filename"]);
                    }

                    using (var context = Context.Create("Provider=SQLite; Uri=file:///c:/Temp/test.db"))
                    {
                        Assert.AreEqual("SQLite", context.Properties["Provider"]);
                        Assert.AreEqual("c:\\Temp\\test.db", context.Properties["Ht4n.SQLite.Filename"]);
                    }

                    using (var context = Context.Create("Provider=SQLite; Uri=\"file:///c/My Documents/Temp/test.db\""))
                    {
                        Assert.AreEqual("SQLite", context.Properties["Provider"]);
                        Assert.AreEqual("c:\\My Documents\\Temp\\test.db", context.Properties["Ht4n.SQLite.Filename"]);
                    }

                    using (var context = Context.Create("Provider=SQLite; Uri=file:///c/My%20Documents/Temp/test.db"))
                    {
                        Assert.AreEqual("SQLite", context.Properties["Provider"]);
                        Assert.AreEqual("c:\\My Documents\\Temp\\test.db", context.Properties["Ht4n.SQLite.Filename"]);
                    }
                }
                else
                {
                    using (var context = Context.Create("Provider=SQLite; Uri=file:///tmp/test.db"))
                    {
                        Assert.AreEqual("SQLite", context.Properties["Provider"]);
                        Assert.AreEqual("/tmp/test.db", context.Properties["Ht4n.SQLite.Filename"]);
                    }

                    using (var context = Context.Create("Provider=SQLite; Uri=\"file:///tmp/My Documents/test.db\""))
                    {
                        Assert.AreEqual("SQLite", context.Properties["Provider"]);
                        Assert.AreEqual("/tmp/My Documents/test.db", context.Properties["Ht4n.SQLite.Filename"]);
                    }

                    using (var context = Context.Create("Provider=SQLite; Uri=file:///tmp/My%20Documents/test.db"))
                    {
                        Assert.AreEqual("SQLite", context.Properties["Provider"]);
                        Assert.AreEqual("/tmp/My Documents/test.db", context.Properties["Ht4n.SQLite.Filename"]);
                    }
                }

                using (var context = Context.Create("Provider=SQLite; Uri=file://" + (windows ? "%TEMP%" : "%HOME%") + "/test.db"))
                {
                    Assert.AreEqual("SQLite", context.Properties["Provider"]);
                    Assert.IsTrue(((string)context.Properties["Ht4n.SQLite.Filename"]).EndsWith(Path.DirectorySeparatorChar + "test.db"));
                }

                using (var context = Context.Create("--Provider SQLite --Ht4n.SQLite.Filename \"My Documents/test.db\""))
                {
                    Assert.AreEqual("SQLite", context.Properties["Provider"]);
                    Assert.AreEqual("My Documents/test.db", context.Properties["Ht4n.SQLite.Filename"]);
                }

                using (var context = Context.Create("--Provider SQLite --Ht4n.SQLite.Filename \"C:\\My Documents\\test.db\""))
                {
                    Assert.AreEqual("SQLite", context.Properties["Provider"]);
                    Assert.AreEqual("C:\\My Documents\\test.db", context.Properties["Ht4n.SQLite.Filename"]);
                }

                using (var context = Context.Create("Provider=SQLite; Uri=file://test.db; Ht4n.SQLite.PageSizeKB=16; Ht4n.SQLite.CacheSizeMB=32; Ht4n.SQLite.Synchronous=yes"))
                {
                    Assert.AreEqual("SQLite", context.Properties["Provider"]);
                    Assert.AreEqual(16, context.Properties["Ht4n.SQLite.PageSizeKB"]);
                    Assert.AreEqual(32, context.Properties["Ht4n.SQLite.CacheSizeMB"]);
                    Assert.IsTrue((bool)context.Properties["Ht4n.SQLite.Synchronous"]);
                    Assert.IsFalse((bool)context.Properties["Ht4n.SQLite.Index.Column"]);
                    Assert.IsFalse((bool)context.Properties["Ht4n.SQLite.Index.ColumnFamily"]);
                    Assert.IsFalse((bool)context.Properties["Ht4n.SQLite.Index.ColumnQualifier"]);
                    Assert.IsFalse((bool)context.Properties["Ht4n.SQLite.Index.Timestamp"]);
                }
            }
        }

        [TestMethod]
        public void DeleteFile()
        {
            if (Context.HasContext(ContextKind.SQLite))
            {
                string filename;
                using (var context = Context.Create("Provider=SQLite; Uri=file://test.sqlite"))
                using (var client = context.CreateClient())
                {
                    Assert.IsTrue(client.NamespaceExists("/"));
                    filename = (string)context.Properties["Ht4n.SQLite.Filename"];
                    Assert.IsTrue(File.Exists(filename));
                }

                Assert.IsNotNull(filename);
                File.Delete(filename);
            }
        }

        [TestMethod]
        public void MultipleClients()
        {
            using (var context = Context.Create(connectionString))
            {
                for (var i = 0; i < 5; ++i)
                {
                    using (var client = context.CreateClient())
                    {
                        Assert.IsTrue(client.NamespaceExists("/"));
                    }
                }

                for (var i = 0; i < 5; ++i)
                {
                    using (var clientA = context.CreateClient())
                    using (var clientB = context.CreateClient())
                    {
                        Assert.IsTrue(clientA.NamespaceExists("/"));
                        Assert.IsTrue(clientB.NamespaceExists("/"));
                    }
                }
            }
        }

        [TestMethod]
        public void MultipleContexts()
        {
            using (var contextA = Context.Create(connectionString))
            using (var contextB = Context.Create(connectionString))
            {
                using (var clientA = contextA.CreateClient())
                using (var clientB = contextB.CreateClient())
                {
                    Assert.IsTrue(clientA.NamespaceExists("/"));
                    Assert.IsTrue(clientB.NamespaceExists("/"));
                }
            }

            using (var contextA = Context.Create(connectionString))
            using (var clientA = contextA.CreateClient())
            using (var contextB = Context.Create(connectionString))
            using (var clientB = contextB.CreateClient())
            {
                Assert.IsTrue(clientA.NamespaceExists("/"));
                Assert.IsTrue(clientB.NamespaceExists("/"));
            }

            var t1 = new Thread(
                        () =>
                        {
                            using (var context = Context.Create(connectionString))
                            {
                                Thread.Sleep(100);
                                using (var client = context.CreateClient())
                                {
                                    Assert.IsTrue(client.NamespaceExists("/"));
                                    Thread.Sleep(100);
                                }
                            }
                        });

            var t2 = new Thread(
                () =>
                {
                    new Thread(
                        () =>
                        {
                            using (var context = Context.Create(connectionString))
                            {
                                Thread.Sleep(100);
                                using (var client = context.CreateClient())
                                {
                                    Assert.IsTrue(client.NamespaceExists("/"));
                                    Thread.Sleep(100);
                                }
                            }
                        });
                });

            t1.Start();
            t2.Start();
            t1.Join();
            t2.Join();
        }

        [TestMethod]
        public void Reconnect()
        {
            for (var i = 0; i < 5; ++i)
            {
                using (var context = Context.Create(connectionString))
                {
                    using (var client = context.CreateClient())
                    {
                        Assert.IsTrue(client.NamespaceExists("/"));
                    }
                }

                Thread.Sleep(200);

                using (var context = Context.Create(connectionString))
                {
                    using (var client = context.CreateClient())
                    {
                        Assert.IsTrue(client.NamespaceExists("/"));
                    }
                }

                Thread.Sleep(200);
            }
        }
    }
}
