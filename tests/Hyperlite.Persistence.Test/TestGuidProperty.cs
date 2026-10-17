namespace Hyperlite.Persistence.Test
{
    using System;

    using Hyperlite.Persistence.Test.TestGuidPropertyTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestGuidPropertyTypes
    {
        using System;

        using Hyperlite.Persistence.Attributes;

        [Entity("TestEntityManager", ColumnFamily = "a")]
        internal sealed class EntityA
        {
            public EntityA()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public string Name { get; set; }

            [Id]
            public Guid RowKey { get; private set; }

            public override bool Equals(object o)
            {
                if (ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is EntityA))
                {
                    return false;
                }

                return string.Equals(this.Name, (o as EntityA).Name);
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }
        }

        [Entity("TestEntityManager", ColumnFamily = "b")]
        internal sealed class EntityB
        {
            public EntityB()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public EntityA A { get; set; }

            public string Name { get; set; }

            [Id]
            public Guid RowKey { get; private set; }

            public override bool Equals(object o)
            {
                if (ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is EntityB))
                {
                    return false;
                }

                return string.Equals(this.Name, (o as EntityB).Name) && (this.A == null ? (o as EntityB).A == null : this.A.Equals((o as EntityB).A));
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode() ^ (this.A != null ? this.A.GetHashCode() : 0);
            }
        }
    }

    /// <summary>
    ///     An entity identified by a Guid property rather than by a string.
    /// </summary>
    [TestClass]
    public sealed class TestGuidProperty : TestBase
    {
        [TestMethod]
        public void GuidKeysAreAssignedAndFound()
        {
            var eb1 = new EntityB();
            TestBase.TestSerialization(eb1);

            var eb2 = new EntityB { A = new EntityA() };
            TestBase.TestSerialization(eb2);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(eb1);
                Assert.IsFalse(eb1.RowKey.Equals(Guid.Empty));

                em.Persist(eb2);
                Assert.IsFalse(eb2.RowKey.Equals(Guid.Empty));
                Assert.IsFalse(eb2.A.RowKey.Equals(Guid.Empty));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _eb1 = em.Find<EntityB>(eb1.RowKey);
                Assert.AreEqual(eb1, _eb1);

                var _ea2 = em.Find<EntityA>(eb2.A.RowKey);
                Assert.AreEqual(eb2.A, _ea2);

                var _eb2 = em.Find<EntityB>(eb2.RowKey);
                Assert.AreEqual(eb2, _eb2);
            }
        }
    }
}
