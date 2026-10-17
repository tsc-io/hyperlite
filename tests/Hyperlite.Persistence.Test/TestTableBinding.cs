namespace Hyperlite.Persistence.Test
{
    using Hyperlite;
    using Hyperlite.Persistence.Test.TestTableBindingTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestTableBindingTypes
    {
        using System;

        internal sealed class EntityA
        {
            public EntityA()
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

        internal sealed class TableBindingEntityA : ITableBinding
        {
            public string Namespace
            {
                get
                {
                    return null;
                }
            }

            public string TableName
            {
                get
                {
                    return "TestEntityManager";
                }
            }
        }

        internal sealed class EntityB
        {
            public EntityB()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public EntityA A { get; set; }

            public Key Key { get; set; }

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

        internal sealed class TableBindingEntityB : ITableBinding
        {
            public string Namespace
            {
                get
                {
                    return null;
                }
            }

            public string TableName
            {
                get
                {
                    return "EntityB";
                }
            }
        }
    }

    /// <summary>
    ///     Supplying the namespace and table through ITableBinding rather than through the
    ///     Entity attribute.
    /// </summary>
    [TestClass]
    public sealed class TestTableBinding : TestBase
    {
        [TestMethod]
        public void TablesComeFromTheBinding()
        {
            var bindings = Emf.Configuration.Binding;
            Assert.IsFalse(bindings.StrictExplicitColumnBinding);
            Assert.IsFalse(bindings.StrictExplicitKeyBinding);
            Assert.IsFalse(bindings.StrictExplicitTableBinding);

            Assert.IsTrue(bindings.RegisterTableBinding(typeof(EntityA), new TableBindingEntityA()));
            Assert.IsFalse(bindings.RegisterTableBinding(typeof(EntityA), new TableBindingEntityA()));
            Assert.IsTrue(bindings.RegisterTableBinding(typeof(EntityB), new TableBindingEntityB()));

            var eb1 = new EntityB();
            TestBase.TestSerialization(eb1);

            var eb2 = new EntityB { A = new EntityA() };
            TestBase.TestSerialization(eb2);

            using (var em = Emf.CreateEntityManager())
            {
                Assert.IsTrue(em.IsTypeDeclared<EntityA>());
                Assert.IsTrue(em.IsTypeDeclared<EntityB>());

                em.Configuration.Binding.StrictExplicitTableBinding = true;

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

                Assert.IsNotNull(eb2.A.Key);
                Assert.IsFalse(string.IsNullOrEmpty(eb2.A.Key.Row));
                Assert.IsFalse(string.IsNullOrEmpty(eb2.A.Key.ColumnFamily));
                Assert.IsTrue(string.IsNullOrEmpty(eb2.A.Key.ColumnQualifier));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _eb1 = em.Find<EntityB>(eb1.Key);
                Assert.AreEqual(eb1, _eb1);

                var _ea2 = em.Find<EntityA>(eb2.A.Key);
                Assert.AreEqual(eb2.A, _ea2);

                var _eb2 = em.Find<EntityB>(eb2.Key);
                Assert.AreEqual(eb2, _eb2);
            }

            Assert.IsTrue(bindings.UnregisterTableBinding(typeof(EntityA)));
            Assert.IsFalse(bindings.UnregisterTableBinding(typeof(EntityA)));
            Assert.IsTrue(bindings.UnregisterTableBinding(typeof(EntityB)));
        }
    }
}
