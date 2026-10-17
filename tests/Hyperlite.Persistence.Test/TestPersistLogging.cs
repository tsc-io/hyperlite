namespace Hyperlite.Persistence.Test.TestPersistLoggingTypes
{
    using System;

    using Hyperlite.Persistence.Attributes;

    [Entity(ColumnFamily = "a")]
    internal sealed class EntityA
    {
        public EntityA()
        {
            this.Name = Guid.NewGuid().ToString();
            this.Value = new byte[16];
        }

        public Guid Id { get; private set; }

        public string Name { get; set; }

        public byte[] Value { get; set; }

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

    [Entity(ColumnFamily = "b")]
    internal sealed class EntityB
    {
        public EntityB()
        {
            this.Name = Guid.NewGuid().ToString();
            this.Value = new byte[16];
        }

        public Guid Id { get; private set; }

        public string Name { get; set; }

        public byte[] Value { get; set; }

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

            return string.Equals(this.Name, (o as EntityB).Name);
        }

        public override int GetHashCode()
        {
            return this.Name.GetHashCode();
        }
    }
}

namespace Hyperlite.Persistence.Test
{
    using System;

    using Hyperlite;
    using Hyperlite.Persistence.Serialization;
    using Hyperlite.Persistence.Test.Common;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using EntityA = Hyperlite.Persistence.Test.TestPersistLoggingTypes.EntityA;
    using EntityB = Hyperlite.Persistence.Test.TestPersistLoggingTypes.EntityB;

    /// <summary>
    ///     What a persist writes to the trace, at the default settings and at the ones
    ///     tuned for throughput.
    /// </summary>
    [TestClass]
    public sealed class TestPersistLogging : TestBase
    {
        [TestMethod]
        public void PersistLogging()
        {
            var ea = new EntityA();
            TestBase.TestSerialization(ea);

            var eb = new EntityB();
            TestBase.TestSerialization(eb);

            for (var j = 0; j < 5; ++j)
            {
                using (var em = Emf.CreateEntityManager())
                {
                    em.Configuration.MutatorSpec = new MutatorSpec(MutatorKind.Chunked)
                    {
                        Queued = true,
                        //// MaxCellCount = 8 * 1024,
                        MaxChunkSize = 64 * 1024,
                        FlushEachChunk = true,
                        Capacity = 32768
                    };

                    for (var i = 0; i < 100000; ++i)
                    {
                        ea = new EntityA();
                        Rng.Instance.NextBytes(ea.Value);
                        em.Persist(ea, Behaviors.CreateNew | Behaviors.DoNotCache);

                        eb = new EntityB();
                        Rng.Instance.NextBytes(eb.Value);
                        em.Persist(eb, Behaviors.CreateNew | Behaviors.DoNotCache);
                    }
                }
            }
        }

        [TestMethod]
        public void PersistLoggingMaxPerformance()
        {
            var ea = new EntityA();
            TestBase.TestSerialization(ea);

            var eb = new EntityB();
            TestBase.TestSerialization(eb);

            var rng = new Random();
            for (var j = 0; j < 5; ++j)
            {
                using (var em = Emf.CreateEntityManager())
                {
                    var mutatorSpec = new MutatorSpec(MutatorKind.Chunked)
                    {
                        Queued = true,
                        //// MaxCellCount = 8 * 1024,
                        MaxChunkSize = 64 * 1024,
                        FlushEachChunk = true,
                        Capacity = 32768
                    };

                    using (var ma = em.GetTable<EntityA>().CreateMutator(mutatorSpec))
                    using (var mb = em.GetTable<EntityB>().CreateMutator(mutatorSpec))
                    {
                        for (var i = 0; i < 100000; ++i)
                        {
                            ea = new EntityA();
                            rng.NextBytes(ea.Value);
                            ma.Set(new Key { ColumnFamily = "a" }, Serializer.ToByteArray(ea), true);

                            eb = new EntityB();
                            rng.NextBytes(eb.Value);
                            mb.Set(new Key { ColumnFamily = "b" }, Serializer.ToByteArray(ea), true);
                        }
                    }
                }
            }
        }
    }
}
