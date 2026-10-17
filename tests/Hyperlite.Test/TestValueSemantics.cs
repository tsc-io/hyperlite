namespace Hyperlite.Test
{
    using System.Collections.Generic;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Equality, ordering and hashing for the value types a scan is built from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A coverage run put <c>RowInterval</c> at 30%, <c>CellInterval</c> at 33% and
    ///         <c>ColumnPredicate</c> at 58%, and what was uncovered is exactly the part
    ///         carrying a compatibility claim. Each of those files says its hash combiner or
    ///         its ordering is the one ht4n used and is kept that way on purpose, and
    ///         nothing asserted any of it. <c>CellInterval.Equals(object)</c> goes further
    ///         and documents a deliberate *deviation* from ht4n, with the words "No test
    ///         covered it".
    ///     </para>
    ///     <para>
    ///         What can be pinned and what cannot is worth stating, because the claim in
    ///         those comments is broader than reality. The combiner is deterministic; the
    ///         values it combines are not. <c>string.GetHashCode</c> is randomised per
    ///         process on .NET, so any interval holding a row key hashes differently in
    ///         every run - measured, 2030311780 in one process and -1124953865 in the next
    ///         for the same <c>RowInterval("r1", "r2")</c>. So the absolute hash of anything
    ///         containing a string cannot be pinned here or compared with ht4n, and a
    ///         hash code must never be persisted. What is pinned is the combiner itself,
    ///         through instances built only from booleans and enums, plus the contract that
    ///         actually matters at runtime: equal values hash equally.
    ///     </para>
    /// </remarks>
    [TestClass]
    public class TestValueSemantics
    {
        //
        // RowInterval
        //

        /// <summary>
        ///     The hash combiner, pinned through values that contain no string.
        /// </summary>
        /// <remarks>
        ///     Booleans hash to 1 and 0 on every runtime, so an interval with null rows has
        ///     a hash that depends only on the combiner. If these numbers move, the
        ///     combiner was changed - which the comment on <c>RowInterval.Hash</c> says it
        ///     must not be.
        /// </remarks>
        [TestMethod]
        public void RowIntervalCombinerIsUnchanged()
        {
            Assert.AreEqual(14326, new RowInterval(null, true, null, false).GetHashCode());
            Assert.AreEqual(14298, new RowInterval(null, false, null, true).GetHashCode());
            Assert.AreEqual(14327, new RowInterval(null, true, null, true).GetHashCode());
            Assert.AreEqual(14297, new RowInterval(null, false, null, false).GetHashCode());
        }

        /// <summary>
        ///     Equal intervals hash equally, which is the only hash property callers may
        ///     rely on.
        /// </summary>
        [TestMethod]
        public void EqualRowIntervalsHashEqually()
        {
            var a = new RowInterval("r1", true, "r2", false);
            var b = new RowInterval("r1", true, "r2", false);

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreEqual(a.GetHashCode(), new RowInterval(a).GetHashCode(), "the copy constructor");

            // and each component participates, so two intervals differing in one of them
            // are not equal
            Assert.AreNotEqual(a, new RowInterval("r1", true, "r3", false));
            Assert.AreNotEqual(a, new RowInterval("r0", true, "r2", false));
            Assert.AreNotEqual(a, new RowInterval("r1", false, "r2", false));
            Assert.AreNotEqual(a, new RowInterval("r1", true, "r2", true));
        }

        /// <summary>
        ///     The operators agree with Equals, including for null on either side.
        /// </summary>
        /// <remarks>
        ///     An operator that dereferences its argument throws where the caller expects
        ///     false, and <c>x == null</c> is how most code tests these.
        /// </remarks>
        [TestMethod]
        public void RowIntervalOperatorsHandleNull()
        {
            var a = new RowInterval("r1", "r2");
            RowInterval nothing = null;

            Assert.IsTrue(a == new RowInterval("r1", "r2"));
            Assert.IsFalse(a != new RowInterval("r1", "r2"));

            Assert.IsFalse(a == nothing);
            Assert.IsTrue(a != nothing);
            Assert.IsFalse(nothing == a);
            Assert.IsTrue(nothing != a);
            Assert.IsTrue(nothing == null);
            Assert.IsFalse(nothing != null);

            Assert.IsFalse(a.Equals(null));
            Assert.IsFalse(a.Equals("not an interval"));
            Assert.IsTrue(a.Equals((object)new RowInterval("r1", "r2")));
        }

        //
        // CellInterval
        //

        /// <summary>
        ///     <c>Equals(object)</c> agrees with <c>Equals(CellInterval)</c>.
        /// </summary>
        /// <remarks>
        ///     This is the deliberate deviation from ht4n that its own comment says no test
        ///     covered: ht4n's override returned the negation of the comparison, so the two
        ///     overloads disagreed and everything routing through object equality - a
        ///     dictionary key, Contains, Distinct, Assert.AreEqual - got the opposite
        ///     answer. Asserted here through each of those routes, because the bug was
        ///     invisible to a direct Equals call.
        /// </remarks>
        [TestMethod]
        public void CellIntervalObjectEqualityIsNotInverted()
        {
            var a = new CellInterval("r1", "cf1", "r2", "cf2");
            var same = new CellInterval("r1", "cf1", "r2", "cf2");
            var other = new CellInterval("r1", "cf1", "r9", "cf2");

            // the typed overload
            Assert.IsTrue(a.Equals(same));
            Assert.IsFalse(a.Equals(other));

            // and the object one has to say the same thing
            Assert.IsTrue(a.Equals((object)same), "Equals(object) disagrees with Equals(CellInterval)");
            Assert.IsFalse(a.Equals((object)other), "Equals(object) is inverted");

            // which is what these depend on
            Assert.AreEqual(a, same);
            Assert.AreNotEqual(a, other);

            var set = new HashSet<CellInterval> { a };
            Assert.IsTrue(set.Contains(same), "an equal interval is not found in a set");
            Assert.IsFalse(set.Contains(other));

            var map = new Dictionary<CellInterval, string> { { a, "value" } };
            Assert.AreEqual("value", map[same], "an equal interval does not find its entry");

            var list = new List<CellInterval> { a, same, other };
            Assert.AreEqual(2, new HashSet<CellInterval>(list).Count, "Distinct collapsed the wrong pair");

            Assert.IsFalse(a.Equals(null));
            Assert.IsFalse(a.Equals("not an interval"));
        }

        /// <summary>
        ///     Ordering is consistent with equality and with the operators.
        /// </summary>
        [TestMethod]
        public void CellIntervalOrdersConsistently()
        {
            var low = new CellInterval("r1", "cf1", "r2", "cf2");
            var high = new CellInterval("r2", "cf1", "r3", "cf2");
            var same = new CellInterval("r1", "cf1", "r2", "cf2");

            Assert.AreEqual(0, low.CompareTo(same));
            Assert.IsTrue(low.CompareTo(high) < 0);
            Assert.IsTrue(high.CompareTo(low) > 0);

            // null sorts first, so a non-null instance is greater than it
            Assert.IsTrue(low.CompareTo(null) > 0);

            Assert.IsTrue(low < high);
            Assert.IsTrue(high > low);
            Assert.IsFalse(low < same);
            Assert.IsFalse(low > same);

            // comparison and equality agree
            Assert.AreEqual(low.Equals(same), low.CompareTo(same) == 0);
            Assert.AreEqual(low.Equals(high), low.CompareTo(high) == 0);

            // and sorting a list is stable and correct
            var list = new List<CellInterval> { high, low, same };
            list.Sort();
            Assert.AreEqual(low, list[0]);
            Assert.AreEqual(high, list[2]);
        }

        //
        // ColumnPredicate
        //

        /// <summary>
        ///     Search values order by length first and only then bytewise.
        /// </summary>
        /// <remarks>
        ///     "Orders by length first, then bytewise, which is what ht4n did" - so a one
        ///     byte 0xff sorts *before* a two byte 0x00 0x00, which is the opposite of what
        ///     a plain lexicographic comparison gives and the reason the claim is worth
        ///     pinning. Someone replacing this with a memcmp-style compare would pass every
        ///     equality test and change the order of every scan spec.
        /// </remarks>
        [TestMethod]
        public void SearchValuesOrderByLengthFirst()
        {
            var shortButLarge = Predicate(new byte[] { 0xff });
            var longButSmall = Predicate(new byte[] { 0x00, 0x00 });

            Assert.IsTrue(
                shortButLarge.CompareTo(longButSmall) < 0,
                "a shorter search value must sort first even when its bytes are larger");
            Assert.IsTrue(longButSmall.CompareTo(shortButLarge) > 0);

            // same length, then bytewise
            Assert.IsTrue(Predicate(new byte[] { 0x01, 0x00 }).CompareTo(Predicate(new byte[] { 0x01, 0x01 })) < 0);
            Assert.IsTrue(Predicate(new byte[] { 0x01, 0x01 }).CompareTo(Predicate(new byte[] { 0x01, 0x00 })) > 0);
            Assert.AreEqual(0, Predicate(new byte[] { 0x01, 0x02 }).CompareTo(Predicate(new byte[] { 0x01, 0x02 })));

            // empty and null are distinguishable from each other and from a value
            Assert.AreEqual(0, Predicate(new byte[0]).CompareTo(Predicate(new byte[0])));
            Assert.IsTrue(Predicate(new byte[0]).CompareTo(Predicate(new byte[] { 0x00 })) < 0);
            Assert.AreEqual(0, Predicate(null).CompareTo(Predicate(null)));
        }

        /// <summary>
        ///     Ordering runs family, then qualifier, then match kind, then search value.
        /// </summary>
        /// <remarks>
        ///     The order of the keys matters: a predicate differing in its family must sort
        ///     by that and ignore everything after it, or two scan specs that should be
        ///     equivalent compare differently.
        /// </remarks>
        [TestMethod]
        public void PredicateOrderingPrefersEarlierKeys()
        {
            // a later key cannot outrank an earlier one
            var earlyFamily = new ColumnPredicate("a", "zzz", MatchKind.QualifierRegex, new byte[] { 0xff });
            var lateFamily = new ColumnPredicate("b", "aaa", MatchKind.ValueExact, new byte[0]);
            Assert.IsTrue(earlyFamily.CompareTo(lateFamily) < 0, "family does not win over qualifier");

            var earlyQualifier = new ColumnPredicate("a", "a", MatchKind.QualifierRegex, new byte[] { 0xff });
            var lateQualifier = new ColumnPredicate("a", "b", MatchKind.ValueExact, new byte[0]);
            Assert.IsTrue(earlyQualifier.CompareTo(lateQualifier) < 0, "qualifier does not win over match");

            var lowMatch = new ColumnPredicate("a", "a", MatchKind.ValueExact, new byte[] { 0xff });
            var highMatch = new ColumnPredicate("a", "a", MatchKind.QualifierExact, new byte[0]);
            Assert.IsTrue(lowMatch.CompareTo(highMatch) < 0, "match does not win over search value");

            Assert.IsTrue(lowMatch.CompareTo(null) > 0);
            Assert.AreEqual(0, lowMatch.CompareTo(lowMatch));
        }

        /// <summary>
        ///     Equal predicates hash equally, search value included.
        /// </summary>
        /// <remarks>
        ///     The search value is hashed by content, not by reference - two distinct arrays
        ///     holding the same bytes have to hash the same, or a predicate stops finding
        ///     itself in a dictionary.
        /// </remarks>
        [TestMethod]
        public void EqualPredicatesHashEqually()
        {
            var a = new ColumnPredicate("cf", "cq", MatchKind.ValueExact, new byte[] { 1, 2, 3 });
            var b = new ColumnPredicate("cf", "cq", MatchKind.ValueExact, new byte[] { 1, 2, 3 });

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode(), "the search value is hashed by reference");
            Assert.AreEqual(a.GetHashCode(), new ColumnPredicate(a).GetHashCode(), "the copy constructor");

            Assert.AreNotEqual(a, new ColumnPredicate("cf", "cq", MatchKind.ValueExact, new byte[] { 1, 2, 4 }));
            Assert.AreNotEqual(a, new ColumnPredicate("cf", "cq", MatchKind.ValuePrefix, new byte[] { 1, 2, 3 }));

            var map = new Dictionary<ColumnPredicate, string> { { a, "value" } };
            Assert.AreEqual("value", map[b]);
        }

        /// <summary>
        ///     The byte-array hash combiner, pinned through a predicate holding no string.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Same reasoning as <see cref="RowIntervalCombinerIsUnchanged" />: with
        ///         both names null the hash depends only on the match kind and the search
        ///         value, so it is deterministic across processes and runtimes. The comment
        ///         on <c>HashArray</c> says the values match ht4n's; these numbers are what
        ///         that means in practice, and they were read off a run rather than
        ///         derived - which is the only honest way to pin a value whose definition is
        ///         "whatever the predecessor produced".
        ///     </para>
        ///     <para>
        ///         Note the third and fourth: a one byte array hashes the same whatever the
        ///         byte is. ht4n's loop was <c>while (--len) hash ^= 617 * (*p++)</c>, which
        ///         visits len - 1 bytes and never looks at the last one, so a single byte is
        ///         never looked at at all. It is kept deliberately - a hash only has to be
        ///         consistent - and pinned here because it reads like a defect and the next
        ///         person to notice it should find a test saying it is not.
        ///     </para>
        /// </remarks>
        [TestMethod]
        public void SearchValueHashCombinerIsUnchanged()
        {
            Assert.AreEqual(493, Predicate(null).GetHashCode());
            Assert.AreEqual(14328, Predicate(new byte[0]).GetHashCode());
            Assert.AreEqual(14914, Predicate(new byte[] { 1, 2 }).GetHashCode());
            Assert.AreEqual(16020, Predicate(new byte[] { 1, 2, 3 }).GetHashCode());

            // the skipped last byte: one element arrays collide with each other, and the
            // last element of any array does not affect the hash
            Assert.AreEqual(14297, Predicate(new byte[] { 0 }).GetHashCode());
            Assert.AreEqual(
                Predicate(new byte[] { 0 }).GetHashCode(),
                Predicate(new byte[] { 255 }).GetHashCode(),
                "ht4n's loop skips the last byte, so a single byte never reaches the hash");
            Assert.AreEqual(
                Predicate(new byte[] { 1, 2 }).GetHashCode(),
                Predicate(new byte[] { 1, 99 }).GetHashCode(),
                "the last byte of an array does not reach the hash");

            // and equality is unaffected by any of that - it compares the bytes
            Assert.AreNotEqual(Predicate(new byte[] { 0 }), Predicate(new byte[] { 255 }));
            Assert.AreNotEqual(Predicate(new byte[] { 1, 2 }), Predicate(new byte[] { 1, 99 }));
        }

        /// <summary>
        ///     A predicate with no strings, so that its hash is deterministic.
        /// </summary>
        private static ColumnPredicate Predicate(byte[] searchValue)
        {
            return new ColumnPredicate
            {
                ColumnFamily = null,
                ColumnQualifier = null,
                Match = MatchKind.Undefined,
                SearchValue = searchValue
            };
        }
    }
}
