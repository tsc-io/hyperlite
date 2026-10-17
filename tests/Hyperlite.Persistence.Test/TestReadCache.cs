namespace Hyperlite.Persistence.Test
{
    using System.Linq;

    using Hyperlite.Persistence.Test.TestReadCacheTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestReadCacheTypes
    {
        using System;
        using System.Collections.Generic;

        using Hyperlite.Persistence.Attributes;
        using Hyperlite.Persistence.Test.Common;

        [Entity("TestEntityManager")]
        internal abstract class Base
        {
            protected Base(string id)
            {
                this.Id = id;
                this.Name = Guid.NewGuid().ToString();
            }

            public string Id { get; private set; }

            public string Name { get; set; }

            public override bool Equals(object o)
            {
                if (ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is Base))
                {
                    return false;
                }

                return string.Equals(this.Name, (o as Base).Name);
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }
        }

        [Entity("TestEntityManager", ColumnFamily = "a")]
        [Serializable]
        internal sealed class Parent : Base
        {
            public Parent(string id)
                : base(id)
            {
                this.Children = new List<Child>();
            }

            public List<Child> Children { get; private set; }

            public override bool Equals(object o)
            {
                if (object.ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is Parent))
                {
                    return false;
                }

                return base.Equals(o) && Equatable.AreEqual(this.Children, (o as Parent).Children);
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }
        }

        [Entity("TestEntityManager", ColumnFamily = "b")]
        [Serializable]
        internal sealed class Child : Base
        {
            public Child(string id)
                : base(id)
            {
            }
        }
    }

    /// <summary>
    ///     The read cache. A fetch that returns a parent and its children hands out one
    ///     instance per entity, so the children reached through the parent are the same
    ///     objects as the ones the fetch delivered.
    /// </summary>
    [TestClass]
    public sealed class TestReadCache : TestBase
    {
        [TestMethod]
        public void FetchHandsOutOneInstancePerEntity()
        {
            var p = new Parent("0");
            p.Children.Add(new Child("X"));
            p.Children.Add(new Child("Y"));
            p.Children.Add(new Child("Z"));
            TestBase.TestSerialization(p);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(p, Behaviors.CreateNew);
                Assert.AreEqual("0", p.Id);
                Assert.AreEqual("X", p.Children[0].Id);
                Assert.AreEqual("Y", p.Children[1].Id);
                Assert.AreEqual("Z", p.Children[2].Id);
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _p = em.Find<Parent>(p.Id);
                Assert.AreEqual(p, _p);
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _b = em.Fetch<Base>(new[] { typeof(Parent), typeof(Child) });
                var _p = _b.OfType<Parent>().First();
                Assert.AreEqual(p, _p);

                var i = 0;
                foreach (var c in _b.OfType<Child>().OrderBy(c => c.Id))
                {
                    Assert.AreEqual(p.Children[i], c);
                    Assert.AreSame(_p.Children[i++], c);
                }
            }
        }
    }
}
