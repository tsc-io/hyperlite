namespace Hyperlite.Test
{
    using System;
    using System.Threading;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Writing the same cell twice, which leaves both revisions in the table, newest first.
    /// </summary>
    /// <remarks>
    ///     Each scenario below is written once, as a private method taking a
    ///     <see cref="MutatorSpec" />, and run by four tests: with no spec, chunked, queued, and
    ///     chunked and queued together. A scenario that passes unbuffered and fails chunked is
    ///     the interesting case, and naming the four separately is what makes the report say
    ///     which buffering mode broke.
    /// </remarks>
    [TestClass]
    public sealed class TestTableMutatorRevisions : TableMutatorFixture
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            CreateTable(typeof(TestTableMutatorRevisions));
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            DropTable();
        }

        [TestInitialize]
        public void TestInitialize()
        {
            ContinueExecution();

            // every scenario asserts an empty table on entry
            Delete(table);
        }

        [TestMethod]
        public void Revisions()
        {
            this.Revisions(null);
        }

        [TestMethod]
        public void RevisionsChunked()
        {
            this.Revisions(ChunkedMutatorSpec);
        }

        [TestMethod]
        public void RevisionsChunkedQueued()
        {
            this.Revisions(ChunkedQueuedMutatorSpec);
        }

        [TestMethod]
        public void RevisionsQueued()
        {
            this.Revisions(MutatorSpec.CreateQueued());
        }

        private void Revisions(MutatorSpec mutatorSpec)
        {
            Assert.AreEqual(0, this.GetCellCount());

            var key = new Key(Guid.NewGuid().ToString()) { ColumnFamily = "a", ColumnQualifier = "tag" };

            using (var mutator = table.CreateMutator(mutatorSpec))
            {
                mutator.Set(key, Encoding.GetBytes(key.Row));
                mutator.Flush();

                Cell cell1;
                Cell cellNone;
                using (var scanner = table.CreateScanner(new ScanSpec(key.Row).AddColumn("a")))
                {
                    Assert.IsTrue(scanner.Next(out cell1));
                    Assert.IsFalse(scanner.Next(out cellNone));
                }

                Assert.AreEqual(cell1.Key.Row, Encoding.GetString(cell1.Value));

                mutator.Set(key, Encoding.GetBytes("abc"));
                mutator.Flush();

                Cell cell2;
                using (var scanner = table.CreateScanner(new ScanSpec(key.Row).AddColumn("a")))
                {
                    Assert.IsTrue(scanner.Next(out cell1));
                    Assert.IsTrue(scanner.Next(out cell2));
                    Assert.IsFalse(scanner.Next(out cellNone));
                }

                Assert.IsTrue(cell1.Key.Timestamp > cell2.Key.Timestamp);
                Assert.AreEqual("abc", Encoding.GetString(cell1.Value));
                Assert.AreEqual(cell2.Key.Row, Encoding.GetString(cell2.Value));

                cell1.Value = Encoding.GetBytes("def");
                mutator.Set(cell1); // does NOT create a new revision
                mutator.Flush();

                Cell cell1_1;
                using (var scanner = table.CreateScanner(new ScanSpec(key.Row).AddColumn("a")))
                {
                    Assert.IsTrue(scanner.Next(out cell1_1));
                    Assert.IsTrue(scanner.Next(out cell2));
                    Assert.IsFalse(scanner.Next(out cellNone));
                }

                Assert.IsTrue(cell1.Key == cell1_1.Key);
                Assert.AreEqual(cell1.Key, cell1_1.Key);
                Assert.AreEqual(0, cell1.Key.CompareTo(cell1_1.Key));
                Assert.AreEqual(0, Key.Compare(cell1.Key, cell1_1.Key));
                Assert.AreEqual(cell1.Key.GetHashCode(), cell1_1.Key.GetHashCode());
                Assert.AreEqual("def", Encoding.GetString(cell1_1.Value));
                Assert.AreEqual(cell2.Key.Row, Encoding.GetString(cell2.Value));
            }
        }
    }
}
