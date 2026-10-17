namespace Hyperlite.Persistence.Test
{
    using Hyperlite.Persistence.Test.Common;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     What the behaviors make of an entity this entity manager does not know.
    /// </summary>
    /// <remarks>
    ///     Under Behaviors.CreateLazy such an entity is given a new key - and with
    ///     Behaviors.WriteNewOnly it was then not written, as it did not count as new: it came out
    ///     of the persist carrying a key no row had.
    /// </remarks>
    [TestClass]
    public sealed class TestBehaviors : TestBase
    {
        [TestMethod]
        public void AnEntityGivenANewKeyIsWritten()
        {
            var ec = new EntityC();
            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ec);
            }

            var known = ec.Id;
            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ec, Behaviors.CreateLazy | Behaviors.WriteNewOnly);
                em.Flush();

                Assert.AreNotEqual(known, ec.Id);
                Assert.AreEqual(ec, em.Find<EntityC>(ec.Id));
            }
        }

        /// <summary>
        ///     Any looks without fetching: what it found is still unknown to the manager afterwards,
        ///     so a persist of it is not skipped as fetched.
        /// </summary>
        [TestMethod]
        public void AnyLeavesNothingOnRecord()
        {
            var ec = new EntityC();
            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ec);
            }

            var known = ec.Id;
            using (var em = Emf.CreateEntityManager())
            {
                Assert.IsTrue(em.Any<EntityC>());
                em.Persist(ec);
                em.Flush();

                Assert.AreNotEqual(known, ec.Id);
                Assert.AreEqual(ec, em.Find<EntityC>(ec.Id));
            }
        }

        /// <summary>
        ///     The defaults always carry a create behavior, which the writer assigns keys by: set
        ///     without one, they keep the one they had.
        /// </summary>
        [TestMethod]
        public void ADefaultWithoutACreateBehaviorKeepsTheCurrentOne()
        {
            using (var em = Emf.CreateEntityManager())
            {
                em.DefaultBehaviors = Behaviors.WriteOnce;
                Assert.AreEqual(Behaviors.CreateLazy | Behaviors.WriteOnce, em.DefaultBehaviors);

                em.DefaultBehaviors = Behaviors.CreateNew;
                em.DefaultBehaviors = Behaviors.DoNotCache;
                Assert.AreEqual(Behaviors.CreateNew | Behaviors.DoNotCache, em.DefaultBehaviors);
            }
        }
    }
}
