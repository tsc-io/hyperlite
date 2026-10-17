namespace Hyperlite.Persistence.Test
{
    using System;
    using System.Linq;

    using Hyperlite.Persistence.Attributes;
    using Hyperlite.Persistence.Serialization;
    using Hyperlite.Persistence.Test.TestFetchFailureTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestFetchFailureTypes
    {
        [Entity("TestFetchFailure", ColumnFamily = "a")]
        internal sealed class Holder
        {
            public string Id { get; private set; }

            public object Payload { get; set; }
        }

        /// <summary>
        ///     Neither an entity nor declared by one - a holder's payload is an object - so
        ///     strict types refuse to read it.
        /// </summary>
        internal sealed class Undeclared
        {
            public string Name { get; set; }
        }
    }

    /// <summary>
    ///     A fetch that fails throws what it failed with, however many entities it reads.
    /// </summary>
    /// <remarks>
    ///     Scans of more than 32 rows, or of no row limit, deserialize on parallel tasks, and their
    ///     failures surfaced wrapped in an <see cref="AggregateException" /> - so the exception a
    ///     caller saw depended on the size of the scan.
    /// </remarks>
    [TestClass]
    public sealed class TestFetchFailure : TestBase
    {
        [TestMethod]
        public void AParallelFetchThrowsWhatASingleFindThrows()
        {
            EnsureTable("TestFetchFailure", "<Schema><AccessGroup name=\"default\"><ColumnFamily><Name>a</Name></ColumnFamily></AccessGroup></Schema>");

            string id = null;
            using (var em = Emf.CreateEntityManager())
            {
                for (var i = 0; i < 40; ++i)
                {
                    var holder = new Holder { Payload = new Undeclared { Name = "n" + i } };
                    em.Persist(holder);
                    id = holder.Id;
                }
            }

            using (var em = Emf.CreateEntityManager())
            {
                Assert.ThrowsExactly<SerializationException>(() => em.Find<Holder>(id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                Assert.ThrowsExactly<SerializationException>(() => em.Fetch<Holder>().ToList());
            }
        }
    }
}
