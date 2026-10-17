namespace Hyperlite.Persistence.Test
{
    using System.Collections.Generic;
    using System.Linq;

    using Hyperlite;
    using Hyperlite.Persistence.Test.TestEntityAttributeTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestEntityAttributeTypes
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
            public string RowKey { get; private set; }

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

            public Key Key { get; private set; }

            public string Name { get; set; }

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

        [Entity("TestEntityManager", ColumnFamily = "c", ColumnQualifier = "1")]
        internal sealed class EntityC1
        {
            public EntityC1()
            {
                this.Name = Guid.NewGuid().ToString();
                this.Key = new Key();
            }

            public Key Key { get; private set; }

            public string Name { get; set; }

            public override bool Equals(object o)
            {
                if (ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is EntityC1))
                {
                    return false;
                }

                return string.Equals(this.Name, (o as EntityC1).Name);
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }
        }

        [Entity("TestEntityManager", ColumnFamily = "c", ColumnQualifier = "2")]
        internal sealed class EntityC2
        {
            public EntityC2()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public EntityC1 C1 { get; set; }

            public Key Key { get; private set; }

            public string Name { get; set; }

            public override bool Equals(object o)
            {
                if (ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is EntityC2))
                {
                    return false;
                }

                return string.Equals(this.Name, (o as EntityC2).Name) && (this.C1 == null ? (o as EntityC2).C1 == null : this.C1.Equals((o as EntityC2).C1));
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode() ^ (this.C1 != null ? this.C1.GetHashCode() : 0);
            }
        }
    }

    /// <summary>
    ///     The Entity attribute: the table it names, the column family and qualifier it
    ///     places an entity in, and the behaviours a persist can be given.
    /// </summary>
    [TestClass]
    public sealed class TestEntityAttribute : TestBase
    {
        [TestMethod]
        public void AttributeDecidesTableAndColumn()
        {
            var eb1 = new EntityB();
            TestBase.TestSerialization(eb1);

            var eb2 = new EntityB { A = new EntityA() };
            TestBase.TestSerialization(eb2);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(eb1);
                Assert.IsNotNull(eb1.Key);
                Assert.IsFalse(string.IsNullOrEmpty(eb1.Key.Row));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.Key.ColumnFamily));
                Assert.IsTrue(string.IsNullOrEmpty(eb1.Key.ColumnQualifier));

                em.Persist(eb2);
                Assert.IsNotNull(eb2.Key);
                Assert.IsFalse(string.IsNullOrEmpty(eb2.Key.Row));
                Assert.IsFalse(string.IsNullOrEmpty(eb2.Key.ColumnFamily));
                Assert.IsTrue(string.IsNullOrEmpty(eb2.Key.ColumnQualifier));

                Assert.IsFalse(string.IsNullOrEmpty(eb2.A.RowKey));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _eb1 = em.Find<EntityB>(eb1.Key);
                Assert.AreEqual(eb1, _eb1);

                var _ea2 = em.Find<EntityA>(eb2.A.RowKey);
                Assert.AreEqual(eb2.A, _ea2);

                var _eb2 = em.Find<EntityB>(eb2.Key);
                Assert.AreEqual(eb2, _eb2);
            }

            var ec1 = new EntityC1();
            var ec2 = new EntityC2 { C1 = new EntityC1() };

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ec1);
                Assert.IsNotNull(ec1.Key);
                Assert.IsFalse(string.IsNullOrEmpty(ec1.Key.Row));
                Assert.IsFalse(string.IsNullOrEmpty(ec1.Key.ColumnFamily));
                Assert.IsFalse(string.IsNullOrEmpty(ec1.Key.ColumnQualifier));

                em.Persist(ec2);
                Assert.IsNotNull(ec2.Key);
                Assert.IsFalse(string.IsNullOrEmpty(ec2.Key.Row));
                Assert.IsFalse(string.IsNullOrEmpty(ec2.Key.ColumnFamily));
                Assert.IsFalse(string.IsNullOrEmpty(ec2.Key.ColumnQualifier));

                Assert.IsNotNull(ec2.C1);
                Assert.IsFalse(string.IsNullOrEmpty(ec2.C1.Key.Row));
                Assert.IsFalse(string.IsNullOrEmpty(ec2.C1.Key.ColumnFamily));
                Assert.IsFalse(string.IsNullOrEmpty(ec2.C1.Key.ColumnQualifier));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _ec1 = em.Find<EntityC1>(ec1.Key);
                Assert.AreEqual(ec1, _ec1);

                var _ec12 = em.Find<EntityC1>(ec2.C1.Key);
                Assert.AreEqual(ec2.C1, _ec12);

                var _ec2 = em.Find<EntityC2>(ec2.Key);
                Assert.AreEqual(ec2, _ec2);
            }
        }

        /// <summary>
        ///     The default behaviour, persisted and read back, then persisted again over a
        ///     cleared namespace and after each of the create behaviours in turn.
        /// </summary>
        /// <remarks>
        ///     Left as one test because it is progressive: the entities are declared once and
        ///     each phase depends on what the previous one left, so the phases are not
        ///     independent the way the three below are.
        /// </remarks>
        [TestMethod]
        public void DefaultBehaviorAcrossRepeatedPersists()
        {
            var ea1 = new EntityA();
            TestBase.TestSerialization(ea1);

            var eb1 = new EntityB();
            TestBase.TestSerialization(eb1);

            var eb2 = new EntityB { A = ea1 };
            TestBase.TestSerialization(eb2);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ea1);
                em.Persist(eb1);
                em.Persist(eb2);
            }

            using (var em = Emf.CreateEntityManager())
            {
                Assert.AreEqual(1, em.Fetch<EntityA>().Count());
                Assert.AreEqual(2, em.Fetch<EntityB>().Count());

                var _ea1 = em.Find<EntityA>(ea1.RowKey);
                Assert.AreEqual(ea1, _ea1);

                var _eb1 = em.Find<EntityB>(eb1.Key);
                Assert.AreEqual(eb1, _eb1);

                var _eb2 = em.Find<EntityB>(eb2.Key);
                Assert.AreEqual(eb2, _eb2);

                var _ea2 = em.Find<EntityA>(eb2.A.RowKey);
                Assert.AreEqual(ea1, _ea2);
            }

            TestBase.ClearNamespace();

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(ea1);
            }

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(eb1);
                em.Persist(eb2);
            }

            using (var em = Emf.CreateEntityManager())
            {
                Assert.AreEqual(2, em.Fetch<EntityA>().Count());
                Assert.AreEqual(2, em.Fetch<EntityB>().Count());

                var _ea1 = em.Find<EntityA>(ea1.RowKey);
                Assert.AreEqual(ea1, _ea1);

                var _eb1 = em.Find<EntityB>(eb1.Key);
                Assert.AreEqual(eb1, _eb1);

                var _eb2 = em.Find<EntityB>(eb2.Key);
                Assert.AreEqual(eb2, _eb2);

                var _ea2 = em.Find<EntityA>(eb2.A.RowKey);
                Assert.AreEqual(eb2.A, _ea2);
            }

            TestBase.ClearNamespace();

            using (var em = Emf.CreateEntityManager())
            {
                var ida = new HashSet<string>();
                var idb = new HashSet<string>();

                em.Persist(ea1, Behaviors.CreateLazy);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateLazy);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateLazy);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                em.Persist(ea1, Behaviors.CreateLazy);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateLazy);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateLazy);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                Assert.AreEqual(1, ida.Count);
                Assert.AreEqual(2, idb.Count);

                em.Flush();

                Assert.AreEqual(1, em.Fetch<EntityA>().Count());
                Assert.AreEqual(2, em.Fetch<EntityB>().Count());
            }

            TestBase.ClearNamespace();

            using (var em = Emf.CreateEntityManager())
            {
                var ida = new HashSet<string>();
                var idb = new HashSet<string>();

                em.Persist(ea1, Behaviors.CreateAlways);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateAlways);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateAlways);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                em.Persist(ea1, Behaviors.CreateAlways);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateAlways);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateAlways);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                Assert.AreEqual(4, ida.Count);
                Assert.AreEqual(4, idb.Count);

                em.Flush();

                Assert.AreEqual(4, em.Fetch<EntityA>().Count());
                Assert.AreEqual(4, em.Fetch<EntityB>().Count());
            }

            TestBase.ClearNamespace();

            using (var em = Emf.CreateEntityManager())
            {
                var ida = new HashSet<string>();
                var idb = new HashSet<string>();

                em.Persist(ea1, Behaviors.CreateNew);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateNew);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateNew);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                em.Persist(ea1, Behaviors.CreateNew);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateNew);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateNew);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                Assert.AreEqual(1, ida.Count);
                Assert.AreEqual(2, idb.Count);

                em.Flush();

                Assert.AreEqual(1, em.Fetch<EntityA>().Count());
                Assert.AreEqual(2, em.Fetch<EntityB>().Count());
            }
        }

        /// <summary>
        ///     Behaviors.CreateLazy, persisted twice - the ids must not change and the counts
        ///     must not grow.
        /// </summary>
        [TestMethod]
        public void PersistCreateLazy()
        {
            var ea1 = new EntityA();
            TestBase.TestSerialization(ea1);

            var eb1 = new EntityB();
            TestBase.TestSerialization(eb1);

            var eb2 = new EntityB { A = ea1 };
            TestBase.TestSerialization(eb2);

            using (var em = Emf.CreateEntityManager())
            {
                var ida = new HashSet<string>();
                var idb = new HashSet<string>();

                em.Persist(ea1, Behaviors.CreateLazy);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateLazy);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateLazy);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                em.Persist(ea1, Behaviors.CreateLazy);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateLazy);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateLazy);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                Assert.AreEqual(1, ida.Count);
                Assert.AreEqual(2, idb.Count);

                em.Flush();

                Assert.AreEqual(1, em.Fetch<EntityA>().Count());
                Assert.AreEqual(2, em.Fetch<EntityB>().Count());
            }
        }

        /// <summary>
        ///     Behaviors.CreateAlways, persisted twice.
        /// </summary>
        [TestMethod]
        public void PersistCreateAlways()
        {
            var ea1 = new EntityA();
            TestBase.TestSerialization(ea1);

            var eb1 = new EntityB();
            TestBase.TestSerialization(eb1);

            var eb2 = new EntityB { A = ea1 };
            TestBase.TestSerialization(eb2);

            using (var em = Emf.CreateEntityManager())
            {
                var ida = new HashSet<string>();
                var idb = new HashSet<string>();

                em.Persist(ea1, Behaviors.CreateAlways);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateAlways);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateAlways);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                em.Persist(ea1, Behaviors.CreateAlways);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateAlways);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateAlways);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                Assert.AreEqual(4, ida.Count);
                Assert.AreEqual(4, idb.Count);

                em.Flush();

                Assert.AreEqual(4, em.Fetch<EntityA>().Count());
                Assert.AreEqual(4, em.Fetch<EntityB>().Count());
            }
        }

        /// <summary>
        ///     Behaviors.CreateNew, persisted twice.
        /// </summary>
        [TestMethod]
        public void PersistCreateNew()
        {
            var ea1 = new EntityA();
            TestBase.TestSerialization(ea1);

            var eb1 = new EntityB();
            TestBase.TestSerialization(eb1);

            var eb2 = new EntityB { A = ea1 };
            TestBase.TestSerialization(eb2);

            using (var em = Emf.CreateEntityManager())
            {
                var ida = new HashSet<string>();
                var idb = new HashSet<string>();

                em.Persist(ea1, Behaviors.CreateNew);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateNew);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateNew);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                em.Persist(ea1, Behaviors.CreateNew);
                ida.Add(ea1.RowKey);

                em.Persist(eb1, Behaviors.CreateNew);
                idb.Add(eb1.Key.Row);

                em.Persist(eb2, Behaviors.CreateNew);
                idb.Add(eb2.Key.Row);
                ida.Add(eb2.A.RowKey);

                Assert.AreEqual(1, ida.Count);
                Assert.AreEqual(2, idb.Count);

                em.Flush();

                Assert.AreEqual(1, em.Fetch<EntityA>().Count());
                Assert.AreEqual(2, em.Fetch<EntityB>().Count());
            }
        }
    }
}
