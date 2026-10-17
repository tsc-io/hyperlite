namespace Hyperlite.Persistence.Test
{
    using System.Linq;

    using Hyperlite;
    using Hyperlite.Persistence.Test.TestTableAndColumnBindingInheritTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestTableAndColumnBindingInheritTypes
    {
        using System;

        internal abstract class EntityBase
        {
            protected EntityBase()
            {
                this.Key = new Key();
            }

            public Key Key { get; private set; }
        }

        internal abstract class EntityBaseX : EntityBase
        {
        }

        internal sealed class TableBindingEntityBaseX : ITableBinding
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

        internal class EntityAX : EntityBaseX
        {
            public EntityAX()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public string Name { get; set; }

            public override bool Equals(object o)
            {
                if (object.ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is EntityAX))
                {
                    return false;
                }

                return string.Equals(this.Name, (o as EntityAX).Name);
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode();
            }
        }

        internal sealed class ColumnBindingEntityAX : IColumnBinding
        {
            public string ColumnFamily
            {
                get
                {
                    return "a";
                }
            }

            public string ColumnQualifier
            {
                get
                {
                    return null;
                }
            }
        }

        internal sealed class EntityA2X : EntityAX
        {
        }

        internal sealed class ColumnBindingEntityA2X : IColumnBinding
        {
            public string ColumnFamily
            {
                get
                {
                    return "a";
                }
            }

            public string ColumnQualifier
            {
                get
                {
                    return "a2x";
                }
            }
        }

        internal interface IEntityBx
        {
        }

        internal class EntityBX : EntityBaseX, IEntityBx
        {
            public EntityBX()
            {
                this.Name = Guid.NewGuid().ToString();
            }

            public EntityAX A { get; set; }

            public string Name { get; set; }

            public override bool Equals(object o)
            {
                if (object.ReferenceEquals(this, o))
                {
                    return true;
                }

                if (!(o is EntityBX))
                {
                    return false;
                }

                return string.Equals(this.Name, (o as EntityBX).Name) && (this.A == null ? (o as EntityBX).A == null : this.A.Equals((o as EntityBX).A));
            }

            public override int GetHashCode()
            {
                return this.Name.GetHashCode() ^ (this.A != null ? this.A.GetHashCode() : 0);
            }
        }

        internal sealed class ColumnBindingEntityBX : IColumnBinding
        {
            public string ColumnFamily
            {
                get
                {
                    return "b";
                }
            }

            public string ColumnQualifier
            {
                get
                {
                    return null;
                }
            }
        }

        internal sealed class EntityB2X : EntityBX
        {
        }

        internal sealed class ColumnBindingEntityB2X : IColumnBinding
        {
            public string ColumnFamily
            {
                get
                {
                    return "b";
                }
            }

            public string ColumnQualifier
            {
                get
                {
                    return "bx";
                }
            }
        }
    }

    /// <summary>
    ///     ITableBinding and IColumnBinding declared for a base type, and what the types
    ///     deriving from it inherit.
    /// </summary>
    [TestClass]
    public sealed class TestTableAndColumnBindingInherit : TestBase
    {
        [TestMethod]
        public void DerivedTypesInheritTheBaseBindings()
        {
            var bindings = Emf.Configuration.Binding;
            Assert.IsFalse(bindings.StrictExplicitColumnBinding);
            Assert.IsFalse(bindings.StrictExplicitKeyBinding);
            Assert.IsFalse(bindings.StrictExplicitTableBinding);

            Assert.IsTrue(bindings.RegisterTableBinding(typeof(EntityBaseX), new TableBindingEntityBaseX()));
            Assert.IsTrue(bindings.RegisterColumnBinding(typeof(EntityAX), new ColumnBindingEntityAX()));
            Assert.IsTrue(bindings.RegisterColumnBinding(typeof(EntityA2X), new ColumnBindingEntityA2X()));
            Assert.IsTrue(bindings.RegisterColumnBinding(typeof(EntityBX), new ColumnBindingEntityBX()));
            Assert.IsTrue(bindings.RegisterColumnBinding(typeof(EntityB2X), new ColumnBindingEntityB2X()));

            var eb1 = new EntityBX();
            TestBase.TestSerialization(eb1);

            var eb2 = new EntityBX { A = new EntityAX() };
            TestBase.TestSerialization(eb2);

            using (var em = Emf.CreateEntityManager())
            {
                em.Configuration.Binding.StrictExplicitTableBinding = true;
                em.Configuration.Binding.StrictExplicitColumnBinding = true;

                em.Persist(eb1);
                Assert.IsNotNull(eb1.Key);
                Assert.IsFalse(string.IsNullOrEmpty(eb1.Key.Row));
                Assert.AreEqual("b", eb1.Key.ColumnFamily);
                Assert.IsTrue(string.IsNullOrEmpty(eb1.Key.ColumnQualifier));

                em.Persist(eb2);
                Assert.IsNotNull(eb2.Key);
                Assert.IsFalse(string.IsNullOrEmpty(eb2.Key.Row));
                Assert.AreEqual("b", eb2.Key.ColumnFamily);
                Assert.IsTrue(string.IsNullOrEmpty(eb2.Key.ColumnQualifier));

                Assert.IsNotNull(eb2.A.Key);
                Assert.IsFalse(string.IsNullOrEmpty(eb2.A.Key.Row));
                Assert.AreEqual("a", eb2.A.Key.ColumnFamily);
                Assert.IsTrue(string.IsNullOrEmpty(eb2.A.Key.ColumnQualifier));
            }

            using (var em = Emf.CreateEntityManager())
            {
                em.Configuration.Binding.StrictExplicitTableBinding = true;
                em.Configuration.Binding.StrictExplicitColumnBinding = true;

                var _eb1 = em.Find<EntityBX>(eb1.Key);
                Assert.AreEqual(eb1, _eb1);

                var _ea2 = em.Find<EntityAX>(eb2.A.Key);
                Assert.AreEqual(eb2.A, _ea2);

                var _eb2 = em.Find<EntityBX>(eb2.Key);
                Assert.AreEqual(eb2, _eb2);

                Assert.AreEqual(2, em.Fetch<EntityBX>().Count());
                Assert.AreEqual(1, em.Fetch<EntityAX>().Count());
                Assert.AreEqual(3, em.Fetch<EntityBaseX>().Count());
                Assert.AreEqual(2, em.Fetch<IEntityBx>().Count());
                Assert.AreEqual(3, em.Fetch<EntityBaseX>(new[] { typeof(EntityBX), typeof(EntityAX) }).Count());
            }

            var eb21 = new EntityB2X();
            TestBase.TestSerialization(eb21);

            var eb22 = new EntityB2X { A = new EntityA2X() };
            TestBase.TestSerialization(eb22);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(eb21);
                Assert.IsNotNull(eb21.Key);
                Assert.IsFalse(string.IsNullOrEmpty(eb21.Key.Row));
                Assert.AreEqual("b", eb21.Key.ColumnFamily);
                Assert.AreEqual("bx", eb21.Key.ColumnQualifier);

                em.Persist(eb22);
                Assert.IsNotNull(eb22.Key);
                Assert.IsFalse(string.IsNullOrEmpty(eb22.Key.Row));
                Assert.AreEqual("b", eb22.Key.ColumnFamily);
                Assert.AreEqual("bx", eb22.Key.ColumnQualifier);

                Assert.IsNotNull(eb22.A.Key);
                Assert.IsFalse(string.IsNullOrEmpty(eb22.A.Key.Row));
                Assert.AreEqual("a", eb22.A.Key.ColumnFamily);
                Assert.AreEqual("a2x", eb22.A.Key.ColumnQualifier);
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _eb21 = em.Find<EntityBX>(eb21.Key);
                Assert.IsInstanceOfType(_eb21, typeof(EntityB2X));
                Assert.AreEqual(eb21, _eb21);

                var _ea22 = em.Find<EntityAX>(eb22.A.Key);
                Assert.IsInstanceOfType(_ea22, typeof(EntityA2X));
                Assert.AreEqual(eb22.A, _ea22);

                var _eb22 = em.Find<EntityBX>(eb22.Key);
                Assert.IsInstanceOfType(_eb22, typeof(EntityB2X));
                Assert.AreEqual(eb22, _eb22);

                Assert.AreEqual(1, em.Fetch<EntityA2X>().Count());
                Assert.AreEqual(2, em.Fetch<EntityAX>().Count());
                Assert.AreEqual(2, em.Fetch<EntityB2X>().Count());
                Assert.AreEqual(4, em.Fetch<EntityBX>().Count());
                Assert.AreEqual(4, em.Fetch<IEntityBx>().Count());
                Assert.AreEqual(6, em.Fetch<EntityBaseX>().Count());
                Assert.AreEqual(6, em.Fetch<EntityBaseX>(new[] { typeof(EntityBX), typeof(EntityAX) }).Count());
            }

            Assert.IsTrue(bindings.UnregisterColumnBinding(typeof(EntityAX)));
            Assert.IsTrue(bindings.UnregisterColumnBinding(typeof(EntityA2X)));
            Assert.IsTrue(bindings.UnregisterColumnBinding(typeof(EntityBX)));
            Assert.IsTrue(bindings.UnregisterColumnBinding(typeof(EntityB2X)));
            Assert.IsTrue(bindings.UnregisterTableBinding(typeof(EntityBaseX)));
        }
    }
}
