namespace Hyperlite.Persistence.Test
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    using Hyperlite.Persistence.Test.TestCollectionTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestCollectionTypes
    {
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Linq;

        using Hyperlite.Persistence.Attributes;
        using Hyperlite.Persistence.Test.Common;

        [Entity("TestEntityManager", ColumnFamily = "a")]
        internal sealed class EntityX
        {
            public EntityX()
            {
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

                if (!(o is EntityX))
                {
                    return false;
                }

                return string.Equals(this.Name, (o as EntityX).Name);
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }
        }

        [Entity("TestEntityManager", ColumnFamily = "b")]
        internal sealed class EntityY
        {
            public EntityY()
            {
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

                if (!(o is EntityY))
                {
                    return false;
                }

                return string.Equals(this.Name, (o as EntityY).Name);
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }
        }

        [Entity("TestEntityManager")]
        internal class EntityC
        {
            public EntityC()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public string Id { get; private set; }

            public string Name { get; set; }

            public EntityX[] X { get; set; }

            public EntityY[] Y { get; set; }

            public EntityY Y2 { get; set; }

            public override bool Equals(object o)
            {
                if (ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is EntityC))
                {
                    return false;
                }

                var c = o as EntityC;
                if (!string.Equals(this.Name, c.Name))
                {
                    return false;
                }

                return Equatable.AreEqual(this.X, c.X) && Equatable.AreEqual(this.Y, c.Y) && (this.Y2 == null ? c.Y2 == null : this.Y2.Equals(c.Y2));
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }
        }

        [Entity("TestEntityManager")]
        internal sealed class EntityD
        {
            public EntityD()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public string Id { get; private set; }

            public string Name { get; set; }

            public ISet<EntityX> X { get; set; }

            public HashSet<EntityY> Y { get; set; }

            public EntityY Y2 { get; set; }

            public override bool Equals(object o)
            {
                if (ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is EntityD))
                {
                    return false;
                }

                var c = o as EntityD;
                if (!string.Equals(this.Name, c.Name))
                {
                    return false;
                }

                return Equatable.AreEqual(this.X, c.X) && Equatable.AreEqual(this.Y, c.Y) && (this.Y2 == null ? c.Y2 == null : this.Y2.Equals(c.Y2));
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }
        }

        [Entity("TestEntityManager")]
        internal sealed class EntityE
        {
            public EntityE()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public string Id { get; private set; }

            public string Name { get; set; }

            public IList<EntityX> X { get; set; }

            public List<EntityY> Y { get; set; }

            public EntityY Y2 { get; set; }

            public override bool Equals(object o)
            {
                if (ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is EntityE))
                {
                    return false;
                }

                var e = o as EntityE;
                if (!string.Equals(this.Name, e.Name))
                {
                    return false;
                }

                return Equatable.AreEqual(this.X, e.X) && Equatable.AreEqual(this.Y, e.Y) && (this.Y2 == null ? e.Y2 == null : this.Y2.Equals(e.Y2));
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }
        }

        [Entity("TestEntityManager")]
        internal sealed class EntityF
        {
            public EntityF()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public string Id { get; private set; }

            public string Name { get; set; }

            public IList X { get; set; }

            public ArrayList Y { get; set; }

            public EntityY Y2 { get; set; }

            public override bool Equals(object o)
            {
                if (ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is EntityF))
                {
                    return false;
                }

                var f = o as EntityF;
                if (!string.Equals(this.Name, f.Name))
                {
                    return false;
                }

                return ListEquals(this.X, f.X) && ListEquals(this.Y, f.Y) && (this.Y2 == null ? f.Y2 == null : this.Y2.Equals(f.Y2));
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }

            private static bool ListEquals(IList a, IList b)
            {
                if (ReferenceEquals(a, b))
                {
                    return true;
                }

                if (a == null)
                {
                    return false;
                }

                if (a.Count != b.Count)
                {
                    return false;
                }

                for (var i = 0; i < a.Count; ++i)
                {
                    if ((a[i] == null) != (b[i] == null))
                    {
                        return false;
                    }

                    if (a[i] != null && !a[i].Equals(b[i]))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
        /// <summary>
        ///     Dictionaries holding entities as values, as keys, and as both.
        /// </summary>
        [Entity("TestEntityManager")]
        internal sealed class EntityG
        {
            public EntityG()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public string Id { get; private set; }

            public string Name { get; set; }

            public Dictionary<string, EntityX> ByName { get; set; }

            public IDictionary<EntityY, int> Counts { get; set; }

            public Dictionary<EntityX, EntityY> Pairs { get; set; }

            public EntityY Y2 { get; set; }
        }

        /// <summary>
        ///     A collection with an adder and a count, but whose indexer has no setter.
        /// </summary>
        internal sealed class ReadOnlyIndexedList<T> : IEnumerable<T>
        {
            private readonly List<T> items = new List<T>();

            public int Count => this.items.Count;

            public T this[int index] => this.items[index];

            public void Add(T item)
            {
                this.items.Add(item);
            }

            public IEnumerator<T> GetEnumerator()
            {
                return this.items.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }
        }

        /// <summary>
        ///     Entities in a collection whose indexer is read-only.
        /// </summary>
        [Entity("TestEntityManager")]
        internal sealed class EntityH
        {
            public EntityH()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public string Id { get; private set; }

            public string Name { get; set; }

            public ReadOnlyIndexedList<EntityX> X { get; set; }

            public ReadOnlyIndexedList<EntityY> Y { get; set; }

            public EntityY Y2 { get; set; }

            public override bool Equals(object o)
            {
                if (ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is EntityH))
                {
                    return false;
                }

                var h = o as EntityH;
                if (!string.Equals(this.Name, h.Name))
                {
                    return false;
                }

                return SequenceEquals(this.X, h.X) && SequenceEquals(this.Y, h.Y) && (this.Y2 == null ? h.Y2 == null : this.Y2.Equals(h.Y2));
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }

            private static bool SequenceEquals<T>(IEnumerable<T> a, IEnumerable<T> b)
            {
                return a == null ? b == null : b != null && a.SequenceEqual(b);
            }
        }
    }

    /// <summary>
    ///     An entity whose properties are collections of other entities - an array, a set,
    ///     a list and a dictionary - persisted and read back, with the references inside them preserved.
    /// </summary>
    [TestClass]
    public sealed class TestCollection : TestBase
    {
        /// <summary>
        ///     A property declared as an array of entities.
        /// </summary>
        [TestMethod]
        public void EntitiesInAnArray()
        {
            var ec1 = new EntityC();
            TestBase.TestSerialization(ec1);

            var ey = new EntityY();
            var ec2 = new EntityC { X = new[] { new EntityX(), new EntityX() }, Y = new[] { ey, ey }, Y2 = ey };
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

                var _ec2 = em.Find<EntityC>(ec2.Id);
                Assert.AreEqual(ec2, _ec2);
                Assert.AreSame(_ec2.Y[0], _ec2.Y[1]);
                Assert.AreSame(_ec2.Y2, _ec2.Y[0]);
            }
        }

        /// <summary>
        ///     A property declared as ISet&lt;T&gt;, holding a HashSet.
        /// </summary>
        [TestMethod]
        public void EntitiesInASet()
        {
            var ed1 = new EntityD();
            TestBase.TestSerialization(ed1);

            var ey = new EntityY();
            var ed2 = new EntityD { X = new HashSet<EntityX>(new[] { new EntityX(), new EntityX() }), Y = new HashSet<EntityY>(new[] { ey, new EntityY() }), Y2 = ey };
            TestBase.TestSerialization(ed2);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ed1);
                Assert.IsFalse(string.IsNullOrEmpty(ed1.Id));

                em.Persist(ed2);
                Assert.IsFalse(string.IsNullOrEmpty(ed2.Id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _ed1 = em.Find<EntityD>(ed1.Id);
                Assert.AreEqual(ed1, _ed1);

                var _ed2 = em.Find<EntityD>(ed2.Id);
                Assert.AreEqual(ed2, _ed2);
                Assert.IsNotNull(_ed2.Y.FirstOrDefault(item => object.ReferenceEquals(item, _ed2.Y2)));
            }
        }

        /// <summary>
        ///     A property declared as IList&lt;T&gt;, holding a List.
        /// </summary>
        [TestMethod]
        public void EntitiesInAList()
        {
            var ee1 = new EntityE();
            TestBase.TestSerialization(ee1);

            var ey = new EntityY();
            var ee2 = new EntityE { X = new List<EntityX>(new[] { new EntityX(), new EntityX() }), Y = new List<EntityY>(new[] { ey, ey }), Y2 = ey };
            TestBase.TestSerialization(ee2);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ee1);
                Assert.IsFalse(string.IsNullOrEmpty(ee1.Id));

                em.Persist(ee2);
                Assert.IsFalse(string.IsNullOrEmpty(ee2.Id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _ee1 = em.Find<EntityE>(ee1.Id);
                Assert.AreEqual(ee1, _ee1);

                var _ee2 = em.Find<EntityE>(ee2.Id);
                Assert.AreEqual(ee2, _ee2);
                Assert.AreSame(_ee2.Y[0], _ee2.Y[1]);
                Assert.AreSame(_ee2.Y2, _ee2.Y[0]);
            }
        }

        /// <summary>
        ///     A property declared as the non-generic IList, holding an ArrayList.
        /// </summary>
        [TestMethod]
        public void EntitiesInAnUntypedList()
        {
            var ef1 = new EntityF();
            TestBase.TestSerialization(ef1);

            var ey = new EntityY();
            var ef2 = new EntityF { X = new ArrayList(new[] { new EntityX(), new EntityX() }), Y = new ArrayList(new[] { ey, ey }), Y2 = ey };
            TestBase.TestSerialization(ef2);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ef1);
                Assert.IsFalse(string.IsNullOrEmpty(ef1.Id));

                em.Persist(ef2);
                Assert.IsFalse(string.IsNullOrEmpty(ef2.Id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _ef1 = em.Find<EntityF>(ef1.Id);
                Assert.AreEqual(ef1, _ef1);

                var _ef2 = em.Find<EntityF>(ef2.Id);
                Assert.AreEqual(ef2, _ef2);
                Assert.AreSame(_ef2.Y[0], _ef2.Y[1]);
                Assert.AreSame(_ef2.Y2, _ef2.Y[0]);
            }
        }

        /// <summary>
        ///     A property holding a collection whose indexer has no setter.
        /// </summary>
        /// <remarks>
        ///     Such a collection has no usable indexer, so its entities have to be read back
        ///     through its adder - building an indexer setter for it used to throw. Through
        ///     the adder they arrive in the order they are fetched, not the order they were
        ///     stored in.
        /// </remarks>
        [TestMethod]
        public void EntitiesInACollectionWithAReadOnlyIndexer()
        {
            var eh1 = new EntityH();
            TestBase.TestSerialization(eh1);

            var ey = new EntityY();
            var eh2 = new EntityH
            {
                X = new ReadOnlyIndexedList<EntityX> { new EntityX(), new EntityX() },
                Y = new ReadOnlyIndexedList<EntityY> { ey, ey },
                Y2 = ey
            };
            TestBase.TestSerialization(eh2);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(eh1);
                Assert.IsFalse(string.IsNullOrEmpty(eh1.Id));

                em.Persist(eh2);
                Assert.IsFalse(string.IsNullOrEmpty(eh2.Id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _eh1 = em.Find<EntityH>(eh1.Id);
                Assert.AreEqual(eh1, _eh1);

                // read back through the adder, in the order the entities are fetched - so the
                // contents are kept, but not the positions
                var _eh2 = em.Find<EntityH>(eh2.Id);
                Assert.AreEqual(eh2.Name, _eh2.Name);
                CollectionAssert.AreEquivalent(eh2.X.ToList(), _eh2.X.ToList());
                CollectionAssert.AreEqual(eh2.Y.ToList(), _eh2.Y.ToList());
                Assert.AreEqual(eh2.Y2, _eh2.Y2);
                Assert.AreSame(_eh2.Y[0], _eh2.Y[1]);
                Assert.AreSame(_eh2.Y2, _eh2.Y[0]);
            }
        }

        /// <summary>
        ///     Dictionaries holding entities as values, as keys and as both read back, with a
        ///     reference shared between a dictionary and a property preserved.
        /// </summary>
        /// <remarks>
        ///     Writing them always worked; reading threw NotImplementedException, so such data
        ///     was stored and could never be read.
        /// </remarks>
        [TestMethod]
        public void EntitiesInADictionary()
        {
            var x1 = new EntityX();
            var x2 = new EntityX();
            var y1 = new EntityY();
            var y2 = new EntityY();
            var eg = new EntityG
            {
                ByName = new Dictionary<string, EntityX> { { "one", x1 }, { "two", x2 }, { "none", null } },
                Counts = new Dictionary<EntityY, int> { { y1, 1 }, { y2, 2 } },
                Pairs = new Dictionary<EntityX, EntityY> { { x1, y1 }, { x2, y1 } },
                Y2 = y1
            };

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(eg);
                Assert.IsFalse(string.IsNullOrEmpty(eg.Id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var copy = em.Find<EntityG>(eg.Id);
                Assert.IsNotNull(copy);

                Assert.AreEqual(3, copy.ByName.Count);
                Assert.AreEqual(x1, copy.ByName["one"]);
                Assert.AreEqual(x2, copy.ByName["two"]);
                Assert.IsNull(copy.ByName["none"]);

                Assert.AreEqual(2, copy.Counts.Count);
                Assert.AreEqual(1, copy.Counts[y1]);
                Assert.AreEqual(2, copy.Counts[y2]);

                Assert.AreEqual(2, copy.Pairs.Count);
                Assert.AreEqual(y1, copy.Pairs[x1]);
                Assert.AreEqual(y1, copy.Pairs[x2]);

                // one y1 read back, wherever it was referenced from
                Assert.AreSame(copy.Y2, copy.Pairs[x1]);
                Assert.AreSame(copy.Y2, copy.Pairs[x2]);
                Assert.AreSame(copy.Y2, copy.Counts.Keys.Single(y => y.Equals(y1)));
                Assert.AreSame(copy.ByName["one"], copy.Pairs.Keys.Single(x => x.Equals(x1)));
            }
        }

        /// <summary>
        ///     An entry referring to a deleted entity, as its key or as its value, is
        ///     dropped: the entity is never delivered, so the entry is never complete.
        /// </summary>
        [TestMethod]
        public void DictionaryEntriesWithADeletedEntity()
        {
            var gone = new EntityY();
            var kept = new EntityY();
            var goneValue = new EntityX();
            var eg = new EntityG
            {
                Counts = new Dictionary<EntityY, int> { { gone, 1 }, { kept, 2 } },
                ByName = new Dictionary<string, EntityX> { { "gone", goneValue } }
            };

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(eg);
            }

            using (var em = Emf.CreateEntityManager())
            {
                em.Remove(gone);
                em.Remove(goneValue);
            }

            using (var em = Emf.CreateEntityManager())
            {
                var copy = em.Find<EntityG>(eg.Id);
                Assert.AreEqual(1, copy.Counts.Count);
                Assert.AreEqual(2, copy.Counts[kept]);
                Assert.AreEqual(0, copy.ByName.Count);
            }
        }
    }
}
