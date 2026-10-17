namespace Hyperlite.Test
{
    using System.Collections.Generic;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     The helper that reduces a list of columns to its distinct entries, dropping a
    ///     qualifier once the whole family is named.
    /// </summary>
    [TestClass]
    public sealed class TestScanSpec
    {
        [TestMethod]
        public void DistinctColumn()
        {
            var distinctColumn = ScanSpec.DistinctColumn(new[] { "a", "b:1", "b:2", "c:X", "c", "a:1" });
            Assert.AreEqual(4, distinctColumn.Count);
            Assert.IsTrue(distinctColumn.Contains("a"));
            Assert.IsFalse(distinctColumn.Contains("a:1"));
            Assert.IsFalse(distinctColumn.Contains("b"));
            Assert.IsTrue(distinctColumn.Contains("b:1"));
            Assert.IsTrue(distinctColumn.Contains("b:2"));
            Assert.IsTrue(distinctColumn.Contains("c"));
            Assert.IsFalse(distinctColumn.Contains("c:X"));

            ISet<string> columnFamilies;
            distinctColumn = ScanSpec.DistinctColumn(new[] { "a", "b:1", "b:2", "c:X", "c", "a:1" }, out columnFamilies);
            Assert.AreEqual(4, distinctColumn.Count);
            Assert.IsTrue(distinctColumn.Contains("a"));
            Assert.IsFalse(distinctColumn.Contains("a:1"));
            Assert.IsFalse(distinctColumn.Contains("b"));
            Assert.IsTrue(distinctColumn.Contains("b:1"));
            Assert.IsTrue(distinctColumn.Contains("b:2"));
            Assert.IsTrue(distinctColumn.Contains("c"));
            Assert.IsFalse(distinctColumn.Contains("c:X"));

            Assert.AreEqual(3, columnFamilies.Count);
            Assert.IsTrue(columnFamilies.Contains("a"));
            Assert.IsFalse(columnFamilies.Contains("a:1"));
            Assert.IsTrue(columnFamilies.Contains("b"));
            Assert.IsFalse(columnFamilies.Contains("b:1"));
            Assert.IsFalse(columnFamilies.Contains("b:2"));
            Assert.IsTrue(columnFamilies.Contains("c"));
            Assert.IsFalse(columnFamilies.Contains("c:X"));
        }
    }
}
