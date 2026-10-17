namespace Hyperlite.Test
{
    using System;
    using System.IO;
    using System.Linq;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     A schema nested deeper than any schema is refused rather than parsed.
    /// </summary>
    /// <remarks>
    ///     The parser descended recursively without a limit, so a schema nested deeply enough
    ///     overflowed the stack, which ends the process.
    /// </remarks>
    [TestClass]
    public sealed class TestSchemaNesting
    {
        private const string Database = "schema-nesting.db";

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
        public void DeepNestingIsRefused()
        {
            var nested = string.Concat(Enumerable.Repeat("<x>", 100)) + string.Concat(Enumerable.Repeat("</x>", 100));
            this.WithNamespace(ns =>
            {
                var refused = Assert.ThrowsExactly<SchemaParseException>(
                    () => ns.CreateTable("t", Schema("<ColumnFamily><Name>a</Name></ColumnFamily>" + nested)));
                StringAssert.Contains(refused.Message, "nested deeper");
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

        private void WithClient(Action<IClient> body)
        {
            using (var context = Context.Create("Provider=SQLite; Uri=file://" + Database))
            using (var client = context.CreateClient())
            {
                body(client);
            }
        }

        private void WithNamespace(Action<INamespace> body)
        {
            this.WithClient(client =>
            {
                client.CreateNamespace("test", CreateDispositions.CreateIfNotExist);
                using (var ns = client.OpenNamespace("test"))
                {
                    body(ns);
                }
            });
        }
    }
}
