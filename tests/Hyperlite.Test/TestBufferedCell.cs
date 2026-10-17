namespace Hyperlite.Test
{
    using System;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     <see cref="BufferedCell" /> and the scanner overload that fills it, which had no
    ///     coverage at all.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The type exists to avoid an allocation per cell on a long scan: the caller
    ///         keeps one instance, calls <c>Move(cell)</c> repeatedly, and reads the value
    ///         out of a buffer that is reused. That makes two things part of the contract
    ///         which a naive implementation would get wrong, and which no test covered.
    ///     </para>
    ///     <para>
    ///         The buffer is reused, so bytes beyond <see cref="BufferedCell.ValueLength" />
    ///         are whatever the previous cell left there. A caller reading
    ///         <c>Value.Length</c> instead of <c>ValueLength</c> sees stale data from
    ///         another row rather than an exception - so this pins that
    ///         <c>ValueLength</c> is the only honest length, and that the stale tail is
    ///         really there.
    ///     </para>
    ///     <para>
    ///         And the buffer has to grow when a value does not fit, without losing the
    ///         value that caused the growth.
    ///     </para>
    /// </remarks>
    [TestClass]
    public class TestBufferedCell : TestBase
    {
        private const string Schema =
            "<Schema><AccessGroup name=\"default\">" +
            "<ColumnFamily><Name>a</Name></ColumnFamily>" +
            "</AccessGroup></Schema>";

        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>
        ///     A capacity of zero or more than the cell limit allocates nothing up front.
        /// </summary>
        /// <remarks>
        ///     Both are the caller saying "I do not know", and neither should reserve
        ///     memory - the second especially, because Cell.MaxSize is large enough that
        ///     honouring it would be a surprising allocation.
        /// </remarks>
        [TestMethod]
        public void ImplausibleCapacitiesAllocateNothing()
        {
            Assert.IsNull(new BufferedCell(0).Value, "capacity 0 allocated a buffer");
            Assert.IsNull(new BufferedCell(-1).Value, "a negative capacity allocated a buffer");
            Assert.IsNull(
                new BufferedCell(Cell.MaxSize).Value,
                "a capacity at the cell limit allocated a buffer");
            Assert.IsNull(
                new BufferedCell(Cell.MaxSize + 1).Value,
                "a capacity beyond the cell limit allocated a buffer");

            var sized = new BufferedCell(64);
            Assert.IsNotNull(sized.Value, "a plausible capacity allocated nothing");
            Assert.AreEqual(64, sized.Value.Length);
            Assert.AreEqual(0, sized.ValueLength, "an unfilled cell reports a length");
        }

        /// <summary>
        ///     A fresh cell carries no key and the default flag.
        /// </summary>
        [TestMethod]
        public void FreshCellIsEmpty()
        {
            var cell = new BufferedCell(16);

            Assert.IsNull(cell.Key);
            Assert.AreEqual(CellFlag.Default, cell.Flag);
            Assert.AreEqual(0, cell.ValueLength);
            Assert.IsNotNull(cell.ToString(), "ToString on an empty cell threw or returned null");
        }

        /// <summary>
        ///     The scanner rejects a null cell rather than dereferencing it.
        /// </summary>
        [TestMethod]
        public void MoveRejectsNull()
        {
            using (var table = EnsureTable("BufferedCellNull", Schema))
            using (var scanner = table.CreateScanner())
            {
                try
                {
                    scanner.Move((BufferedCell)null);
                    Assert.Fail("Move(null) was accepted");
                }
                catch (ArgumentNullException)
                {
                }
            }
        }

        /// <summary>
        ///     One cell instance carries every row of a scan, and reports each correctly.
        /// </summary>
        [TestMethod]
        public void OneCellCarriesTheWholeScan()
        {
            using (var table = EnsureTable("BufferedCellScan", Schema))
            {
                using (var mutator = table.CreateMutator())
                {
                    for (var i = 0; i < 10; ++i)
                    {
                        mutator.Set(new Key("r" + i, "a"), Utf8.GetBytes("value-" + i));
                    }
                }

                var cell = new BufferedCell(64);
                var seen = 0;

                using (var scanner = table.CreateScanner())
                {
                    while (scanner.Move(cell))
                    {
                        Assert.IsNotNull(cell.Key, "the key was not filled in");
                        Assert.IsTrue(cell.ValueLength > 0, "the value length was not filled in");

                        var expected = "value-" + cell.Key.Row.Substring(1);
                        var actual = Utf8.GetString(cell.Value, 0, cell.ValueLength);
                        Assert.AreEqual(expected, actual, "row " + cell.Key.Row + " read wrongly");

                        ++seen;
                    }
                }

                Assert.AreEqual(10, seen, "the scan did not return every row");
            }
        }

        /// <summary>
        ///     The buffer is reused, and the bytes past ValueLength are the previous cell's.
        /// </summary>
        /// <remarks>
        ///     Pinned because it is the price of the type existing, and it is the mistake
        ///     its users will make: a long value followed by a short one leaves the tail of
        ///     the long one in place, so reading <c>Value.Length</c> bytes returns a mixture
        ///     of two rows. Asserting it here means nobody has to discover it from
        ///     corrupted output.
        /// </remarks>
        [TestMethod]
        public void BufferIsReusedAndTheTailIsStale()
        {
            using (var table = EnsureTable("BufferedCellReuse", Schema))
            {
                using (var mutator = table.CreateMutator())
                {
                    mutator.Set(new Key("r1", "a"), Utf8.GetBytes("aaaaaaaaaaaaaaaaaaaa"));
                    mutator.Set(new Key("r2", "a"), Utf8.GetBytes("b"));
                }

                var cell = new BufferedCell(64);
                byte[] firstBuffer = null;

                using (var scanner = table.CreateScanner())
                {
                    Assert.IsTrue(scanner.Move(cell), "no first cell");
                    Assert.AreEqual("r1", cell.Key.Row);
                    Assert.AreEqual(20, cell.ValueLength);
                    Assert.AreEqual("aaaaaaaaaaaaaaaaaaaa", Utf8.GetString(cell.Value, 0, cell.ValueLength));
                    firstBuffer = cell.Value;

                    Assert.IsTrue(scanner.Move(cell), "no second cell");
                    Assert.AreEqual("r2", cell.Key.Row);
                    Assert.AreEqual(1, cell.ValueLength);
                    Assert.AreEqual("b", Utf8.GetString(cell.Value, 0, cell.ValueLength));

                    Assert.AreSame(
                        firstBuffer,
                        cell.Value,
                        "the buffer was reallocated for a value that fits, which defeats the "
                        + "point of a BufferedCell");

                    // the tail is the previous row's data, still there
                    Assert.AreEqual(
                        (byte)'a',
                        cell.Value[1],
                        "the byte after the short value is not the long value's leftover, so "
                        + "either the buffer is cleared - an avoidable cost - or it was "
                        + "reallocated");
                }
            }
        }

        /// <summary>
        ///     A value larger than the buffer grows it and arrives intact.
        /// </summary>
        [TestMethod]
        public void BufferGrowsForALargerValue()
        {
            using (var table = EnsureTable("BufferedCellGrow", Schema))
            {
                var large = new byte[8192];
                for (var i = 0; i < large.Length; ++i)
                {
                    large[i] = (byte)(i % 251);
                }

                using (var mutator = table.CreateMutator())
                {
                    mutator.Set(new Key("r1", "a"), new byte[] { 1 });
                    mutator.Set(new Key("r2", "a"), large);
                }

                // deliberately too small for the second value
                var cell = new BufferedCell(8);

                using (var scanner = table.CreateScanner())
                {
                    Assert.IsTrue(scanner.Move(cell));
                    Assert.AreEqual(1, cell.ValueLength);

                    Assert.IsTrue(scanner.Move(cell), "the large cell was not returned");
                    Assert.AreEqual(large.Length, cell.ValueLength, "the large value was truncated");
                    Assert.IsTrue(
                        cell.Value.Length >= large.Length,
                        "the buffer did not grow to hold the value");

                    for (var i = 0; i < large.Length; ++i)
                    {
                        if (cell.Value[i] != large[i])
                        {
                            Assert.Fail("the grown buffer differs from the value at byte " + i);
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Move returns false at the end and leaves the cell as it was.
        /// </summary>
        [TestMethod]
        public void MovePastTheEndLeavesTheCellAlone()
        {
            using (var table = EnsureTable("BufferedCellEnd", Schema))
            {
                using (var mutator = table.CreateMutator())
                {
                    mutator.Set(new Key("r1", "a"), Utf8.GetBytes("only"));
                }

                var cell = new BufferedCell(16);

                using (var scanner = table.CreateScanner())
                {
                    Assert.IsTrue(scanner.Move(cell));
                    var row = cell.Key.Row;
                    var length = cell.ValueLength;

                    Assert.IsFalse(scanner.Move(cell), "a second cell was returned");

                    Assert.AreEqual(row, cell.Key.Row, "the key changed after the scan ended");
                    Assert.AreEqual(length, cell.ValueLength, "the length changed after the scan ended");

                    // and it stays exhausted
                    Assert.IsFalse(scanner.Move(cell));
                    Assert.IsFalse(scanner.Move(cell));
                }
            }
        }
    }
}
