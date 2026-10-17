namespace Hyperlite.Test
{
    using System;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Comparing keys, with their timestamp and without it.
    /// </summary>
    [TestClass]
    public sealed class TestKeyComparer
    {
        [TestMethod]
        public void CompareExcludeTimestamp()
        {
            var comparer = new KeyComparer(false);
            var x = new Key();
            Assert.IsFalse(comparer.Equals(x, null));
            Assert.IsFalse(comparer.Equals(null, x));

            var y = new Key();
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));

            x.Row = "A";
            y.Row = "B";
            Assert.IsFalse(comparer.Equals(x, y));

            x.Row = "AB";
            y.Row = "AB";
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));

            x.ColumnFamily = "A";
            y.ColumnFamily = "B";
            Assert.IsFalse(comparer.Equals(x, y));

            x.ColumnFamily = null;
            y.ColumnFamily = string.Empty;
            Assert.IsFalse(comparer.Equals(x, y));

            x.ColumnFamily = "AB";
            y.ColumnFamily = "AB";
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));

            x.ColumnQualifier = "A";
            y.ColumnQualifier = "B";
            Assert.IsFalse(comparer.Equals(x, y));

            x.ColumnQualifier = null;
            y.ColumnQualifier = string.Empty;
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));

            x.ColumnQualifier = "AB";
            y.ColumnQualifier = "AB";
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));

            x.DateTime = new DateTime(2011, 1, 1, 1, 2, 3, DateTimeKind.Utc);
            y.DateTime = new DateTime(2011, 1, 1, 1, 2, 4, DateTimeKind.Utc);
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));

            x.DateTime = new DateTime(2011, 1, 1, 1, 2, 3, DateTimeKind.Utc);
            y.DateTime = new DateTime(2011, 1, 1, 1, 2, 3, DateTimeKind.Utc);
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));
        }

        [TestMethod]
        public void CompareIncludeTimestamp()
        {
            var comparer = new KeyComparer(true);
            var x = new Key();
            Assert.IsFalse(comparer.Equals(x, null));
            Assert.IsFalse(comparer.Equals(null, x));

            var y = new Key();
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));

            x.Row = "A";
            y.Row = "B";
            Assert.IsFalse(comparer.Equals(x, y));

            x.Row = "AB";
            y.Row = "AB";
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));

            x.ColumnFamily = "A";
            y.ColumnFamily = "B";
            Assert.IsFalse(comparer.Equals(x, y));

            x.ColumnFamily = null;
            y.ColumnFamily = string.Empty;
            Assert.IsFalse(comparer.Equals(x, y));

            x.ColumnFamily = "AB";
            y.ColumnFamily = "AB";
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));

            x.ColumnQualifier = "A";
            y.ColumnQualifier = "B";
            Assert.IsFalse(comparer.Equals(x, y));

            x.ColumnQualifier = null;
            y.ColumnQualifier = string.Empty;
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));

            x.ColumnQualifier = "AB";
            y.ColumnQualifier = "AB";
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));

            x.DateTime = new DateTime(2011, 1, 1, 1, 2, 3, DateTimeKind.Utc);
            y.DateTime = new DateTime(2011, 1, 1, 1, 2, 4, DateTimeKind.Utc);
            Assert.IsFalse(comparer.Equals(x, y));

            x.DateTime = new DateTime(2011, 1, 1, 1, 2, 3, DateTimeKind.Utc);
            y.DateTime = new DateTime(2011, 1, 1, 1, 2, 3, DateTimeKind.Utc);
            Assert.IsTrue(comparer.Equals(x, y));
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y));
        }
    }
}
