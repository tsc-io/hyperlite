namespace Hyperlite.Persistence.Test
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Hyperlite;
    using Hyperlite.Persistence.Test.TestUntypedApiTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestUntypedApiTypes
    {
        using System;

        using Hyperlite.Persistence.Attributes;

        [Entity("TestUntypedApi", ColumnFamily = "a")]
        internal class Animal
        {
            public Guid Id { get; private set; }

            public string Name { get; set; }

            public override bool Equals(object o)
            {
                var other = o as Animal;
                return other != null
                       && this.GetType() == other.GetType()
                       && string.Equals(this.Name, other.Name, StringComparison.Ordinal);
            }

            public override int GetHashCode()
            {
                return this.Name == null ? 0 : this.Name.GetHashCode();
            }
        }

        [Entity("TestUntypedApi", ColumnFamily = "b")]
        internal sealed class Dog : Animal
        {
            public string Breed { get; set; }
        }

        [Entity("TestUntypedApi", ColumnFamily = "c")]
        internal sealed class Cat : Animal
        {
            public bool Indoor { get; set; }
        }
    }

    /// <summary>
    ///     The overloads that take a <see cref="Type" /> and an untyped sink.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A coverage run put <c>EntityManager</c> at 52.6%, and the uncovered lines
    ///         were almost entirely one shape: the non-generic overloads. <c>Fetch&lt;T&gt;</c>
    ///         was well covered and <c>Fetch(Type, ...)</c> was not called once, the same for
    ///         <c>FindMany</c>, <c>Remove(Type, ScanSpec)</c> and <c>Merge</c>.
    ///     </para>
    ///     <para>
    ///         That matters because the untyped overloads are what a caller uses when the
    ///         entity type is decided at runtime - a migration tool, an admin screen, a
    ///         generic repository. Each is a short delegation, which is exactly the kind of
    ///         code that is easy to get wrong once and never notice: a swapped argument or
    ///         a behaviours flag dropped on the way through produces no compiler error and
    ///         no failing test.
    ///     </para>
    /// </remarks>
    [TestClass]
    public class TestUntypedApi : TestBase
    {
        /// <summary>
        ///     Creates the table the three entity types share.
        /// </summary>
        /// <remarks>
        ///     Explicitly, because the three live in one table under different column
        ///     families and that is not a shape the implicit creation on first persist
        ///     handles - it raised TableNotFoundException. The suites that need a particular
        ///     schema create it the same way.
        /// </remarks>
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            EnsureTable(
                "TestUntypedApi",
                "<Schema><AccessGroup name=\"default\">"
                + "<ColumnFamily><Name>a</Name></ColumnFamily>"
                + "<ColumnFamily><Name>b</Name></ColumnFamily>"
                + "<ColumnFamily><Name>c</Name></ColumnFamily>"
                + "</AccessGroup></Schema>");
        }

        /// <summary>
        ///     Fetch by Type returns what Fetch of the same type returns.
        /// </summary>
        [TestMethod]
        public void FetchByTypeMatchesTheTypedOverload()
        {
            var expected = Persist(3, i => new Animal { Name = "animal-" + i });

            var typed = new List<Animal>();
            var untyped = new List<object>();

            using (var em = Emf.CreateEntityManager())
            {
                em.Fetch<Animal>(typed.Add);
            }

            using (var em = Emf.CreateEntityManager())
            {
                em.Fetch(typeof(Animal), untyped.Add, Behaviors.Default);
            }

            Assert.AreEqual(expected.Count, typed.Count, "the typed overload is already wrong");
            Assert.AreEqual(
                typed.Count,
                untyped.Count,
                "Fetch(Type, ...) returned a different number of entities than Fetch<T>");

            CollectionAssert.AreEquivalent(
                typed.Cast<object>().ToList(),
                untyped,
                "Fetch(Type, ...) returned different entities than Fetch<T>");

            foreach (var entity in untyped)
            {
                Assert.IsInstanceOfType(entity, typeof(Animal));
            }
        }

        /// <summary>
        ///     Fetch by Type with a scan spec honours the spec.
        /// </summary>
        /// <remarks>
        ///     The spec is the argument most likely to be dropped in a delegation, and
        ///     dropping it returns everything - which looks like success.
        /// </remarks>
        [TestMethod]
        public void FetchByTypeHonoursTheScanSpec()
        {
            Persist(5, i => new Animal { Name = "spec-" + i });

            var all = new List<object>();
            var limited = new List<object>();

            using (var em = Emf.CreateEntityManager())
            {
                em.Fetch(typeof(Animal), all.Add, Behaviors.Default);
                em.Fetch(typeof(Animal), new ScanSpec { MaxRows = 2 }, limited.Add, Behaviors.Default);
            }

            Assert.IsTrue(all.Count >= 5, "the fixture did not persist what it thought");
            Assert.AreEqual(
                2,
                limited.Count,
                "the scan spec was ignored, so Fetch(Type, ScanSpec, ...) returns everything");
        }

        /// <summary>
        ///     Fetch by Type with query types restricts to those types.
        /// </summary>
        [TestMethod]
        public void FetchByTypeHonoursQueryTypes()
        {
            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(new Dog { Name = "rex", Breed = "terrier" });
                em.Persist(new Cat { Name = "tom", Indoor = true });
            }

            var dogsOnly = new List<object>();
            using (var em = Emf.CreateEntityManager())
            {
                em.Fetch(typeof(Animal), new[] { typeof(Dog) }, dogsOnly.Add, Behaviors.Default);
            }

            Assert.IsTrue(dogsOnly.Count > 0, "no entity came back at all");
            foreach (var entity in dogsOnly)
            {
                Assert.IsInstanceOfType(
                    entity,
                    typeof(Dog),
                    "the query types were ignored, a " + entity.GetType().Name + " came back");
            }
        }

        /// <summary>
        ///     A typed sink receives only its own type, even when the query is wider.
        /// </summary>
        /// <remarks>
        ///     This is the filtering path of the private <c>OfType</c> adapter, which turns
        ///     a typed sink into the untyped one the context wants. Asking for Dog with
        ///     query types covering Dog and Cat sends both kinds through the pipeline, and
        ///     the adapter has to drop the Cat rather than cast it and throw. Every typed
        ///     overload goes through this adapter, so the filtering branch is on the path of
        ///     ten public methods and was never exercised.
        /// </remarks>
        [TestMethod]
        public void TypedSinkReceivesOnlyItsOwnType()
        {
            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(new Dog { Name = "filter-dog", Breed = "collie" });
                em.Persist(new Cat { Name = "filter-cat", Indoor = false });
            }

            var dogs = new List<Dog>();
            var everything = new List<object>();

            using (var em = Emf.CreateEntityManager())
            {
                // wider than the sink on purpose
                em.Fetch<Dog>(new[] { typeof(Dog), typeof(Cat) }, dogs.Add);
                em.Fetch(typeof(Animal), new[] { typeof(Dog), typeof(Cat) }, everything.Add, Behaviors.Default);
            }

            Assert.IsTrue(everything.Count >= 2, "the untyped fetch found neither kind");
            Assert.IsTrue(dogs.Count > 0, "the typed fetch found no dog");

            foreach (var dog in dogs)
            {
                Assert.IsInstanceOfType(dog, typeof(Dog), "a non-Dog reached a Dog sink");
            }

            Assert.IsTrue(
                dogs.Count < everything.Count,
                "the typed sink received as much as the untyped one, so nothing was filtered");
        }

        /// <summary>
        ///     FindMany by Type returns the entities named by the keys.
        /// </summary>
        [TestMethod]
        public void FindManyByTypeReturnsTheNamedEntities()
        {
            var persisted = Persist(4, i => new Animal { Name = "find-" + i });

            // a key provider is the entity's own Id, which is how the other suites do it
            var keys = persisted.Take(2).Select(a => (object)a.Id).ToList();

            var found = new List<object>();
            using (var em = Emf.CreateEntityManager())
            {
                em.FindMany(typeof(Animal), keys, found.Add, Behaviors.Default);
            }

            Assert.AreEqual(2, found.Count, "FindMany(Type, ...) did not return one entity per key");
            CollectionAssert.AreEquivalent(
                persisted.Take(2).Cast<object>().ToList(),
                found,
                "FindMany(Type, ...) returned the wrong entities");
        }

        /// <summary>
        ///     Merge writes the entity and hands back the same instance.
        /// </summary>
        /// <remarks>
        ///     <c>EntityContext.Merge</c> carries a <c>TODO implement real merge</c> and
        ///     delegates to <c>Persist(entity, null, Behaviors.CreateNew)</c>, so what is
        ///     asserted here is what it actually promises: the entity is written and the
        ///     argument comes back. Whether it assigns a key is asserted by comparison with
        ///     Persist rather than assumed, because the two go through different behaviours
        ///     and a difference between them is the interesting part.
        /// </remarks>
        [TestMethod]
        public void MergeWritesAndReturns()
        {
            var toMerge = new Animal { Name = "merge-me" };
            var toPersist = new Animal { Name = "persist-me" };

            // a manager each: doing both in one leaves the persisted entity unfound,
            // which is a Merge/Persist interaction worth a test of its own rather than a
            // complication of this one
            Animal merged;
            using (var em = Emf.CreateEntityManager())
            {
                merged = em.Merge(toMerge);
            }

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(toPersist);
            }

            Assert.AreSame(toMerge, merged, "Merge did not hand back the instance it was given");
            Assert.AreEqual("merge-me", merged.Name);

            var found = new List<Animal>();
            using (var em = Emf.CreateEntityManager())
            {
                em.Fetch<Animal>(found.Add);
            }

            Assert.IsTrue(found.Any(a => a.Name == "merge-me"), "the merged entity was not written");
            Assert.IsTrue(found.Any(a => a.Name == "persist-me"), "the persisted entity was not written");

            // Merge and Persist differ here: Persist writes the generated key back onto the
            // entity, Merge leaves the instance's key as it found it. Pinned because it is
            // a real difference between two methods that otherwise look interchangeable,
            // and because a real merge implementation would have to decide about it
            Assert.AreNotEqual(
                Guid.Empty, toPersist.Id, "Persist no longer writes the key back to the entity");
            Assert.AreEqual(
                Guid.Empty,
                toMerge.Id,
                "Merge now writes the key back, which it did not before - if that is the "
                + "real merge being implemented, this assertion is what needs updating");
        }

        /// <summary>
        ///     Remove by Type and scan spec removes only what the spec selects.
        /// </summary>
        [TestMethod]
        public void RemoveByTypeHonoursTheScanSpec()
        {
            Persist(6, i => new Animal { Name = "remove-" + i });

            var before = Count<Animal>();
            Assert.IsTrue(before >= 6, "the fixture did not persist what it thought");

            using (var em = Emf.CreateEntityManager())
            {
                em.Remove(typeof(Animal), new ScanSpec { MaxRows = 2 });
            }

            var after = Count<Animal>();
            Assert.AreEqual(
                before - 2,
                after,
                "Remove(Type, ScanSpec) removed " + (before - after) + " entities where the "
                + "spec selected 2 - either the spec was ignored or the removal was");
        }

        /// <summary>
        ///     The untyped overloads reject a disposed manager, like the typed ones.
        /// </summary>
        /// <remarks>
        ///     Every one of these opens with ThrowIfDisposed, and a delegation written
        ///     without it would work perfectly until the manager was reused after
        ///     disposal - then it would touch a disposed context instead of saying so.
        /// </remarks>
        [TestMethod]
        public void UntypedOverloadsRejectADisposedManager()
        {
            var em = Emf.CreateEntityManager();
            em.Dispose();

            AssertDisposed(() => em.Fetch(typeof(Animal), _ => { }, Behaviors.Default));
            AssertDisposed(() => em.Fetch(typeof(Animal), new ScanSpec(), _ => { }, Behaviors.Default));
            AssertDisposed(() => em.Fetch(typeof(Animal), new[] { typeof(Dog) }, _ => { }, Behaviors.Default));
            AssertDisposed(() => em.FindMany(typeof(Animal), new List<object>(), _ => { }, Behaviors.Default));
            AssertDisposed(() => em.Remove(typeof(Animal), new ScanSpec()));
            AssertDisposed(() => em.Merge(new Animal { Name = "x" }));
        }

        private static void AssertDisposed(Action action)
        {
            try
            {
                action();
                Assert.Fail("a disposed entity manager accepted the call");
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static int Count<T>() where T : class
        {
            var n = 0;
            using (var em = Emf.CreateEntityManager())
            {
                em.Fetch<T>(_ => ++n);
            }

            return n;
        }

        private static List<Animal> Persist(int count, Func<int, Animal> factory)
        {
            var persisted = new List<Animal>();
            using (var em = Emf.CreateEntityManager())
            {
                for (var i = 0; i < count; ++i)
                {
                    var entity = factory(i);
                    em.Persist(entity);
                    persisted.Add(entity);
                }
            }

            return persisted;
        }
    }
}
