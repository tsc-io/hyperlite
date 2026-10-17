namespace Hyperlite.Persistence.Test
{
    using System;

    using Hyperlite.Persistence.Test.Common;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Removing an entity, by the entity and by its key.
    /// </summary>
    [TestClass]
    public sealed class TestRemove : TestBase
    {
        [TestMethod]
        public void RemoveEntity()
        {
            var ec1 = new EntityC();
            TestBase.TestSerialization(ec1);

            var ec2 = new EntityC();
            TestBase.TestSerialization(ec2);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ec1);
                Assert.IsFalse(string.IsNullOrEmpty(ec1.Id));
                em.Persist(ec2);
                Assert.IsFalse(string.IsNullOrEmpty(ec2.Id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _ec1 = em.Find<EntityC>(ec1.Id);
                Assert.AreEqual(ec1, _ec1);

                em.Remove(ec1);
                em.Flush();

                _ec1 = em.Find<EntityC>(ec1.Id);
                Assert.IsNull(_ec1);

                var _ec2 = em.Find<EntityC>(ec2.Id);
                Assert.AreEqual(ec2, _ec2);

                em.Remove(ec2);
                em.Flush();

                _ec2 = em.Find<EntityC>(ec2.Id);
                Assert.IsNull(_ec2);
            }
        }

        [TestMethod]
        public void RemoveKey()
        {
            var ec1 = new EntityC();
            TestBase.TestSerialization(ec1);

            var ec2 = new EntityC();
            TestBase.TestSerialization(ec2);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ec1);
                Assert.IsFalse(string.IsNullOrEmpty(ec1.Id));
                em.Persist(ec2);
                Assert.IsFalse(string.IsNullOrEmpty(ec2.Id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _ec1 = em.Find<EntityC>(ec1.Id);
                Assert.AreEqual(ec1, _ec1);

                em.Remove<EntityC>(ec1.Id);
                em.Flush();

                _ec1 = em.Find<EntityC>(ec1.Id);
                Assert.IsNull(_ec1);

                var _ec2 = em.Find<EntityC>(ec2.Id);
                Assert.AreEqual(ec2, _ec2);

                em.Remove<EntityC>(ec2.Id);
                em.Flush();

                _ec2 = em.Find<EntityC>(ec2.Id);
                Assert.IsNull(_ec2);
            }
        }

        /// <summary>
        ///     An entity removed and persisted again in one entity manager is written again: the
        ///     remove used to leave it on record as written, and the persist skipped it. Under
        ///     the default Behaviors.CreateLazy it is then one the manager does not know, so it is
        ///     written under a new id - as with Behaviors.WriteOnce, which the remove used to leave
        ///     on record as well. Whichever way it is removed: by the entity, by its key, by a scan.
        /// </summary>
        [TestMethod]
        public void RemoveThenPersistWritesAgain()
        {
            var removals = new Action<EntityManager, EntityC>[]
            {
                (em, e) => em.Remove(e),
                (em, e) => em.Remove<EntityC>(e.Id),
                (em, e) => em.Remove<EntityC>(new ScanSpec(e.Id)),
            };

            foreach (var behaviors in new[] { Behaviors.CreateLazy, Behaviors.CreateLazy | Behaviors.WriteOnce })
            {
                foreach (var remove in removals)
                {
                    var ec = new EntityC();
                    using (var em = Emf.CreateEntityManager())
                    {
                        em.Persist(ec, behaviors);

                        // flushed, or a scan would not see what the manager's mutator still holds
                        em.Flush();
                        var removed = ec.Id;
                        remove(em, ec);
                        em.Flush();
                        Assert.IsNull(em.Find<EntityC>(removed), behaviors + " " + Array.IndexOf(removals, remove));

                        em.Persist(ec, behaviors);
                        em.Flush();
                        Assert.AreNotEqual(removed, ec.Id, behaviors.ToString());
                        Assert.AreEqual(ec, em.Find<EntityC>(ec.Id), behaviors.ToString());
                        Assert.IsNull(em.Find<EntityC>(removed), behaviors.ToString());
                    }
                }
            }
        }

        /// <summary>
        ///     Removing by scan spec leaves the caller's spec as it was given.
        /// </summary>
        [TestMethod]
        public void RemoveLeavesTheScanSpecAlone()
        {
            var ec = new EntityC();
            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ec);
                em.Flush();

                var scanSpec = new ScanSpec(ec.Id);
                Assert.IsFalse(scanSpec.KeysOnly);
                em.Remove<EntityC>(scanSpec);
                Assert.IsFalse(scanSpec.KeysOnly);

                em.Flush();
                Assert.IsNull(em.Find<EntityC>(ec.Id));
            }
        }
    }
}
