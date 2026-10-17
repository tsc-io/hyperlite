namespace Hyperlite.Persistence.Test
{
    using System.Collections.Generic;

    using Hyperlite.Persistence.Attributes;
    using Hyperlite.Persistence.Serialization;
    using Hyperlite.Persistence.Test.TestStrictTypesEntitiesTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestStrictTypesEntitiesTypes
    {
        [Entity("TestEntityManager", ColumnFamily = "a")]
        internal sealed class StrictLeaf
        {
            public string Id { get; private set; }

            public string Name { get; set; }
        }

        [Entity("TestEntityManager", ColumnFamily = "b")]
        internal sealed class StrictOwner
        {
            public string Id { get; private set; }

            public StrictLeaf Single { get; set; }

            public List<StrictLeaf> Leaves { get; set; }

            public Dictionary<string, StrictLeaf> ByName { get; set; }
        }
    }

    /// <summary>
    ///     Strict types with entities: an entity type is declared by being one, so entities
    ///     and the entities they refer to read with nothing allowed by hand.
    /// </summary>
    [TestClass]
    public sealed class TestStrictTypesEntities : TestBase
    {
        [TestCleanup]
        public void StrictTypesOn()
        {
            Encoder.Configuration.StrictTypes = true;
        }

        /// <summary>
        ///     Entities read back with strict types on, the default - references, a list and a
        ///     dictionary of entities included.
        /// </summary>
        [TestMethod]
        public void EntitiesReadWithStrictTypes()
        {
            var a = new StrictLeaf { Name = "a" };
            var b = new StrictLeaf { Name = "b" };
            var owner = new StrictOwner
            {
                Single = a,
                Leaves = new List<StrictLeaf> { a, b },
                ByName = new Dictionary<string, StrictLeaf> { { "b", b } }
            };

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(owner);
            }

            using (var em = Emf.CreateEntityManager())
            {
                var copy = em.Find<StrictOwner>(owner.Id);
                Assert.AreEqual("a", copy.Single.Name);
                Assert.AreEqual(2, copy.Leaves.Count);
                Assert.AreSame(copy.Single, copy.Leaves[0]);
                Assert.AreEqual("b", copy.ByName["b"].Name);
            }
        }
    }
}
