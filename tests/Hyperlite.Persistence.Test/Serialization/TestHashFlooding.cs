namespace Hyperlite.Persistence.Test.Serialization
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.Serialization;

    using Hyperlite.Persistence.Serialization;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Strings chosen to collide do not flood the serializer's string reference map.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         That map holds the member names an <c>ISerializable</c> type writes and the
    ///         column family and qualifier of every entity reference - names a type or an
    ///         application chooses at run time, and a qualifier is often data. Ordinary string
    ///         values do not go through it. On .NET Framework x64 <c>string.GetHashCode</c> is
    ///         not randomized and stops at the first <c>'\0'</c>, so <c>"x\0" + anything</c>
    ///         all hash alike - no offline search needed.
    ///     </para>
    ///     <para>
    ///         On the flat table every such insert probed every earlier one, quadratic in the
    ///         number of names. The framework's <c>Dictionary</c>, which the serializer uses
    ///         there now, switches itself to a randomized comparer once a chain passes 100. On
    ///         .NET 8 and later the runtime's hash is randomized and covers the whole string,
    ///         so these do not collide in the first place.
    ///     </para>
    /// </remarks>
    [TestClass]
    public sealed class TestHashFlooding
    {
        /// <summary>
        ///     Enough that quadratic is seconds and linear is milliseconds.
        /// </summary>
        private const int Count = 50000;

        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            InvariantCulture.Pin();
        }

        /// <summary>
        ///     Member names hashing alike serialize in linear time and round-trip.
        /// </summary>
        [TestMethod]
        [Timeout(120000)]
        public void CollidingMemberNamesSerializeInLinearTime()
        {
            var names = Enumerable.Range(0, Count)
                .Select(i => "x\0" + i.ToString(CultureInfo.InvariantCulture))
                .ToArray();

#if NETFRAMEWORK
            // the premise: on this runtime they really do collide
            Assert.AreEqual(names[0].GetHashCode(), names[Count - 1].GetHashCode());
#endif

            var stopwatch = Stopwatch.StartNew();
            var bytes = Serializer.ToByteArray(new ManyMembers(names));
            stopwatch.Stop();

            CollectionAssert.AreEqual(names, Deserializer.FromByteArray<ManyMembers>(bytes).Names);
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                Count + " colliding member names took "
                + stopwatch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)
                + " s to serialize - the string reference map is floodable");
        }

        /// <summary>
        ///     Writes one member per name, which the serializer writes through its string
        ///     reference map.
        /// </summary>
        [Serializable]
        private sealed class ManyMembers : ISerializable
        {
            public ManyMembers(string[] names)
            {
                this.Names = names;
            }

            private ManyMembers(SerializationInfo info, StreamingContext context)
            {
                this.Names = new string[info.MemberCount];
                var i = 0;
                foreach (var entry in info)
                {
                    this.Names[i++] = entry.Name;
                }
            }

            public string[] Names { get; private set; }

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                foreach (var name in this.Names)
                {
                    info.AddValue(name, 0);
                }
            }
        }
    }
}
