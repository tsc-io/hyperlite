namespace Hyperlite.Persistence.Test
{
    using Hyperlite.Persistence.Test.TestCustomEncoderDecoderTypes;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    namespace TestCustomEncoderDecoderTypes
    {
        using Hyperlite.Persistence.Attributes;
        using Hyperlite.Persistence.Serialization;

        [Entity("TestEntityManager", ColumnFamily = "a")]
        internal sealed class EntityA
        {
            public double X;

            public double Y;

            public double Z;

            static EntityA()
            {
                Encoder.Register(
                    100,
                    typeof(EntityA),
                    (serializer, any) =>
                        {
                            var binaryWriter = serializer.BinaryWriter;
                            var e = (EntityA)any;
                            Encoder.WriteDouble(binaryWriter, e.X, false);
                            Encoder.WriteDouble(binaryWriter, e.Y, false);
                            Encoder.WriteDouble(binaryWriter, e.Z, false);
                        },
                    (deserializer) =>
                        {
                            var binaryReader = deserializer.BinaryReader;
                            return new EntityA { X = Decoder.ReadDouble(binaryReader), Y = Decoder.ReadDouble(binaryReader), Z = Decoder.ReadDouble(binaryReader) };
                        });
            }

            public string Id { get; private set; }

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

                var other = (EntityA)o;
                return this.X == other.X && this.Y == other.Y && this.Z == other.Z;
            }

            public override int GetHashCode()
            {
                return this.X.GetHashCode() ^ this.Y.GetHashCode() ^ this.Z.GetHashCode();
            }
        }

        [Entity("TestEntityManager", ColumnFamily = "b")]
        internal sealed class EntityB
        {
            public EntityA A;

            public EntityA B;

            public EntityA C;

            static EntityB()
            {
                Encoder.Register(
                    101,
                    typeof(EntityB),
                    (serializer, any) =>
                        {
                            var e = (EntityB)any;
                            serializer.WriteObject(e.A);
                            serializer.WriteObject(e.B);
                            serializer.WriteObject(e.C);
                        },
                    (deserializer) =>
                        {
                            var eb = new EntityB();
                            deserializer.ReadObject<EntityA>(value => { eb.A = value; });
                            deserializer.ReadObject<EntityA>(value => { eb.B = value; });
                            deserializer.ReadObject<EntityA>(value => { eb.C = value; });
                            return eb;
                        });
            }

            public string Id { get; private set; }

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

                var other = (EntityB)o;
                return (ReferenceEquals(this.A, other.A) || (this.A != null && this.A.Equals(other.A)))
                       && (ReferenceEquals(this.B, other.B) || (this.B != null && this.B.Equals(other.B)))
                       && (ReferenceEquals(this.C, other.C) || (this.C != null && this.C.Equals(other.C)));
            }

            public override int GetHashCode()
            {
                return (this.A != null ? this.A.GetHashCode() : 0) * (this.B != null ? this.B.GetHashCode() : 0) * (this.C != null ? this.C.GetHashCode() : 0);
            }
        }
    }

    /// <summary>
    ///     An entity property whose type has an encoder and a decoder registered for it.
    /// </summary>
    [TestClass]
    public sealed class TestCustomEncoderDecoder : TestBase
    {
        [TestMethod]
        public void EncodedPropertiesRoundTrip()
        {
            var ea1 = new EntityA { X = 1, Y = 2, Z = 3 };
            TestBase.TestSerialization(ea1);

            var ea2 = new EntityA { X = 4, Y = 5, Z = 6 };
            TestBase.TestSerialization(ea2);

            var eb1 = new EntityB { A = ea1, B = ea2 };

            TestBase.TestSerialization(eb1);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(eb1);
                Assert.IsFalse(string.IsNullOrEmpty(eb1.Id));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.A.Id));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.B.Id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _eb1 = em.Find<EntityB>(eb1.Id);
                Assert.AreEqual(eb1, _eb1);
            }

            eb1 = new EntityB { A = ea1, B = ea2, C = ea1 };

            TestBase.TestSerialization(eb1);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(eb1);
                Assert.IsFalse(string.IsNullOrEmpty(eb1.Id));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.A.Id));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.B.Id));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.C.Id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _eb1 = em.Find<EntityB>(eb1.Id);
                Assert.AreEqual(eb1, _eb1);
            }

            eb1 = new EntityB { A = ea1, B = ea1, C = ea2 };

            TestBase.TestSerialization(eb1);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(eb1);
                Assert.IsFalse(string.IsNullOrEmpty(eb1.Id));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.A.Id));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.B.Id));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.C.Id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _eb1 = em.Find<EntityB>(eb1.Id);
                Assert.AreEqual(eb1, _eb1);
            }

            eb1 = new EntityB { A = ea1, B = ea1, C = ea1 };

            TestBase.TestSerialization(eb1);

            using (var em = Emf.CreateEntityManager())
            {
                em.Persist(eb1);
                Assert.IsFalse(string.IsNullOrEmpty(eb1.Id));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.A.Id));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.B.Id));
                Assert.IsFalse(string.IsNullOrEmpty(eb1.C.Id));
            }

            using (var em = Emf.CreateEntityManager())
            {
                var _eb1 = em.Find<EntityB>(eb1.Id);
                Assert.AreEqual(eb1, _eb1);
            }
        }
    }
}
