// The types the serialization fixtures serialize.
//
// Thirty of them, and they were in the middle of the fixture that used them until the
// fixture was split four ways. A namespace of their own rather than the test namespace,
// because the names are deliberately shapes rather than things - ObjectA, StructB,
// IfcAImpl - and they would be a poor set of names to have in scope everywhere.
//
// What each one is for is visible from the case that uses it. What matters here is that
// they are not changed casually: TestFormatCompatibility records the exact bytes some of
// them produce, so adding or reordering a property changes a recorded form and the golden
// file has to be regenerated on purpose.

namespace Hyperlite.Persistence.Test.Serialization.SampleTypes
{
    using System;
    using System.Runtime.Serialization;
    using System.Text;

    using Hyperlite.Persistence.Test.Common;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    internal enum Numbers
    {
        One,

        Two,

        Three
    }

    internal enum CustomNumbers
    {
        One,

        Two,

        Three
    }

    internal interface IfcA
    {
        double Double { get; set; }

        void AssertIsEqualIfcA(object o);

        IfcA Randomize();
    }

    internal interface IfcB : IfcA
    {
        long Long { get; set; }

        void AssertIsEqualIfcB(object o);

        new IfcB Randomize();
    }

    internal struct CustomTypeA
    {
        public double X;

        public int Y;

        public long Z;

        public void AssertIsEqualCustomTypeA(object o)
        {
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(CustomTypeA));
            var cta = (CustomTypeA)o;
            Assert.AreEqual(this.X, cta.X);
            Assert.AreEqual(this.Y, cta.Y);
            Assert.AreEqual(this.Z, cta.Z);
        }
    }

    internal struct StructA
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }

        public void AssertIsEqualObjectA_Nullable(object o)
        {
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(StructA_Nullable));
            var san = (StructA_Nullable)o;
            Assert.IsTrue(Equatable.AreEqual(this.X, san.X));
            Assert.IsTrue(Equatable.AreEqual(this.Y, san.Y));
            Assert.IsTrue(Equatable.AreEqual(this.Z, san.Z));
        }

        public void AssertIsEqualStructA(object o)
        {
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(StructA));
            var sa = (StructA)o;
            Assert.AreEqual(this.X, sa.X);
            Assert.AreEqual(this.Y, sa.Y);
            Assert.AreEqual(this.Z, sa.Z);
        }
    }

    internal struct StructA_Nullable
    {
        public double? X { get; set; }

        public double? Y { get; set; }

        public double? Z { get; set; }
    }

    [Serializable]
    internal struct StructB
    {
        public double X;

        public double Y;

        public double Z;

        public void AssertIsEqualObjectB_Nullable(object o)
        {
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(StructB_Nullable));
            var sbn = (StructB_Nullable)o;
            Assert.IsTrue(Equatable.AreEqual(this.X, sbn.X));
            Assert.IsTrue(Equatable.AreEqual(this.Y, sbn.Y));
            Assert.IsTrue(Equatable.AreEqual(this.Z, sbn.Z));
        }

        public void AssertIsEqualStructB(object o)
        {
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(StructB));
            var sb = (StructB)o;
            Assert.AreEqual(this.X, sb.X);
            Assert.AreEqual(this.Y, sb.Y);
            Assert.AreEqual(this.Z, sb.Z);
        }
    }

    [Serializable]
    internal struct StructB_Nullable
    {
#pragma warning disable 649 // Field is never assigned to, and will always have its default value

        public double? X;

        public double? Y;

        public double? Z;

#pragma warning restore 649
    }

    internal sealed class IfcAImpl : IfcA
    {
        public double Double { get; set; }

        public void AssertIsEqualIfcA(object o)
        {
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(IfcA));
            var ia = (IfcA)o;
            Assert.IsTrue(this.Double.Equals(ia.Double));
        }

        public IfcA Randomize()
        {
            this.Double = Rng.Instance.NextDouble();
            return this;
        }
    }

    internal sealed class IfcAImplOther : IfcA
    {
        public double Double { get; set; }

        public void AssertIsEqualIfcA(object o)
        {
            Assert.IsFalse(object.ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(IfcA));
            var ia = (IfcA)o;
            Assert.IsTrue(this.Double.Equals(ia.Double));
        }

        public IfcA Randomize()
        {
            this.Double = Rng.Instance.NextDouble();
            return this;
        }
    }

    internal sealed class IfcBImpl : IfcB
    {
        public double Double { get; set; }

        public long Long { get; set; }

        public void AssertIsEqualIfcA(object o)
        {
            Assert.IsFalse(object.ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(IfcA));
            var ia = (IfcA)o;
            Assert.IsTrue(this.Double.Equals(ia.Double));
        }

        public void AssertIsEqualIfcB(object o)
        {
            Assert.IsFalse(object.ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(IfcB));
            var ia = (IfcB)o;
            Assert.IsTrue(this.Double.Equals(ia.Double));
            Assert.IsTrue(this.Long.Equals(ia.Long));
        }

        public IfcB Randomize()
        {
            this.Double = Rng.Instance.NextDouble();
            this.Long = (long)(Rng.Instance.Next(int.MaxValue) - int.MaxValue / 2) * Rng.Instance.Next(byte.MaxValue);
            return this;
        }

        IfcA IfcA.Randomize()
        {
            this.Double = Rng.Instance.NextDouble();
            return this;
        }
    }

    internal sealed class IfcBImplOther : IfcB
    {
        public double Double { get; set; }

        public long Long { get; set; }

        public void AssertIsEqualIfcA(object o)
        {
            Assert.IsFalse(object.ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(IfcA));
            var ia = (IfcA)o;
            Assert.IsTrue(this.Double.Equals(ia.Double));
        }

        public void AssertIsEqualIfcB(object o)
        {
            Assert.IsFalse(object.ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(IfcB));
            var ia = (IfcB)o;
            Assert.IsTrue(this.Double.Equals(ia.Double));
            Assert.IsTrue(this.Long.Equals(ia.Long));
        }

        public IfcB Randomize()
        {
            this.Double = Rng.Instance.NextDouble();
            this.Long = (long)(Rng.Instance.Next(int.MaxValue) - int.MaxValue / 2) * Rng.Instance.Next(byte.MaxValue);
            return this;
        }

        IfcA IfcA.Randomize()
        {
            this.Double = Rng.Instance.NextDouble();
            return this;
        }
    }

    internal sealed class ObjectA
    {
        public bool Boolean { get; set; }

        public byte Byte { get; set; }

        public char Char { get; set; }

        public DateTime DateTime { get; set; }

        public decimal Decimal { get; set; }

        public double Double { get; set; }

        public float Float { get; set; }

        public int GetOnly
        {
            get
            {
                return 0;
            }
        }

        public Guid Guid { get; set; }

        public int Int { get; set; }

        public long Long { get; set; }

        public sbyte SByte { get; set; }

        public int SetOnly
        {
            set
            {
            }
        }

        public short Short { get; set; }

        public string String { get; set; }

        public StringBuilder StringBuilder { get; set; }

        public TimeSpan TimeSpan { get; set; }

        public uint UInt { get; set; }

        public ulong ULong { get; set; }

        public ushort UShort { get; set; }

        public void AssertIsEqualObjectA(object o)
        {
            Assert.IsFalse(ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(ObjectA));
            var oa = (ObjectA)o;
            Assert.IsTrue(this.Boolean.Equals(oa.Boolean));
            Assert.IsTrue(this.SByte.Equals(oa.SByte));
            Assert.IsTrue(this.Byte.Equals(oa.Byte));
            Assert.IsTrue(this.Short.Equals(oa.Short));
            Assert.IsTrue(this.UShort.Equals(oa.UShort));
            Assert.IsTrue(this.Int.Equals(oa.Int));
            Assert.IsTrue(this.UInt.Equals(oa.UInt));
            Assert.IsTrue(this.Long.Equals(oa.Long));
            Assert.IsTrue(this.ULong.Equals(oa.ULong));
            Assert.IsTrue(this.Char.Equals(oa.Char));
            Assert.IsTrue(this.Float.Equals(oa.Float));
            Assert.IsTrue(this.Double.Equals(oa.Double));
            Assert.IsTrue(this.Decimal.Equals(oa.Decimal));
            Assert.IsTrue(this.DateTime.Equals(oa.DateTime));
            Assert.IsTrue(this.TimeSpan.Equals(oa.TimeSpan));
            Assert.IsTrue(string.Equals(this.String, oa.String));
            Assert.IsTrue(string.Equals(this.StringBuilder != null ? this.StringBuilder.ToString() : null, oa.StringBuilder != null ? oa.StringBuilder.ToString() : null));
            Assert.IsTrue(this.Guid.Equals(oa.Guid));
        }

        public void AssertIsEqualObjectA_Nullable(object o)
        {
            Assert.IsFalse(ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(ObjectA_Nullable));
            var oan = (ObjectA_Nullable)o;
            Assert.IsTrue(Equatable.AreEqual(this.Boolean, oan.Boolean));
            Assert.IsTrue(Equatable.AreEqual(this.SByte, oan.SByte));
            Assert.IsTrue(Equatable.AreEqual(this.Byte, oan.Byte));
            Assert.IsTrue(Equatable.AreEqual(this.Short, oan.Short));
            Assert.IsTrue(Equatable.AreEqual(this.UShort, oan.UShort));
            Assert.IsTrue(Equatable.AreEqual(this.Int, oan.Int));
            Assert.IsTrue(Equatable.AreEqual(this.UInt, oan.UInt));
            Assert.IsTrue(Equatable.AreEqual(this.Long, oan.Long));
            Assert.IsTrue(Equatable.AreEqual(this.ULong, oan.ULong));
            Assert.IsTrue(Equatable.AreEqual(this.Char, oan.Char));
            Assert.IsTrue(Equatable.AreEqual(this.Float, oan.Float));
            Assert.IsTrue(Equatable.AreEqual(this.Double, oan.Double));
            Assert.IsTrue(Equatable.AreEqual(this.Decimal, oan.Decimal));
            Assert.IsTrue(Equatable.AreEqual(this.DateTime, oan.DateTime));
            Assert.IsTrue(Equatable.AreEqual(this.TimeSpan, oan.TimeSpan));
            Assert.IsTrue(string.Equals(this.String, oan.String));
            Assert.IsTrue(string.Equals(this.StringBuilder != null ? this.StringBuilder.ToString() : null, oan.StringBuilder != null ? oan.StringBuilder.ToString() : null));
            Assert.IsTrue(Equatable.AreEqual(this.Guid, oan.Guid));
        }

        public ObjectA Randomize()
        {
            this.Boolean = (Rng.Instance.Next() % 2) == 0;
            this.SByte = (sbyte)(Rng.Instance.Next(byte.MaxValue) - sbyte.MinValue);
            this.Byte = (byte)Rng.Instance.Next(byte.MaxValue);
            this.Short = (short)(Rng.Instance.Next(ushort.MaxValue) - short.MinValue);
            this.UShort = (ushort)Rng.Instance.Next(ushort.MaxValue);
            this.Int = Rng.Instance.Next(int.MaxValue) - int.MaxValue / 2;
            this.UInt = (uint)Rng.Instance.Next(int.MaxValue);
            this.Long = (long)(Rng.Instance.Next(int.MaxValue) - int.MaxValue / 2) * Rng.Instance.Next(byte.MaxValue);
            this.ULong = (ulong)Rng.Instance.Next(int.MaxValue) * (ulong)Rng.Instance.Next(byte.MaxValue);
            this.Char = (char)Rng.Instance.Next(byte.MaxValue);
            this.Float = (float)Rng.Instance.NextDouble();
            this.Double = Rng.Instance.NextDouble();
            this.Decimal = Rng.Instance.Next(int.MaxValue);
            this.DateTime = DateTime.UtcNow;
            this.TimeSpan = TimeSpan.FromTicks(Rng.Instance.Next(int.MaxValue));
            this.String = Guid.NewGuid().ToString();
            this.StringBuilder = new StringBuilder(Guid.NewGuid().ToString());
            this.Guid = Guid.NewGuid();

            return this;
        }
    }

    internal sealed class ObjectA_Nullable
    {
        public bool? Boolean { get; set; }

        public byte? Byte { get; set; }

        public char? Char { get; set; }

        public DateTime? DateTime { get; set; }

        public decimal? Decimal { get; set; }

        public double? Double { get; set; }

        public float? Float { get; set; }

        public int GetOnly
        {
            get
            {
                return 0;
            }
        }

        public Guid? Guid { get; set; }

        public int? Int { get; set; }

        public long? Long { get; set; }

        public sbyte? SByte { get; set; }

        public int SetOnly
        {
            set
            {
            }
        }

        public short? Short { get; set; }

        public string String { get; set; }

        public StringBuilder StringBuilder { get; set; }

        public TimeSpan? TimeSpan { get; set; }

        public uint? UInt { get; set; }

        public ulong? ULong { get; set; }

        public ushort? UShort { get; set; }

        public void AssertIsEqualObjectA(object o)
        {
            Assert.IsFalse(ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(ObjectA));
            var oa = (ObjectA)o;
            Assert.IsTrue(Equatable.AreEqual(this.Boolean, oa.Boolean));
            Assert.IsTrue(Equatable.AreEqual(this.SByte, oa.SByte));
            Assert.IsTrue(Equatable.AreEqual(this.Byte, oa.Byte));
            Assert.IsTrue(Equatable.AreEqual(this.Short, oa.Short));
            Assert.IsTrue(Equatable.AreEqual(this.UShort, oa.UShort));
            Assert.IsTrue(Equatable.AreEqual(this.Int, oa.Int));
            Assert.IsTrue(Equatable.AreEqual(this.UInt, oa.UInt));
            Assert.IsTrue(Equatable.AreEqual(this.Long, oa.Long));
            Assert.IsTrue(Equatable.AreEqual(this.ULong, oa.ULong));
            Assert.IsTrue(Equatable.AreEqual(this.Char, oa.Char));
            Assert.IsTrue(Equatable.AreEqual(this.Float, oa.Float));
            Assert.IsTrue(Equatable.AreEqual(this.Double, oa.Double));
            Assert.IsTrue(Equatable.AreEqual(this.Decimal, oa.Decimal));
            Assert.IsTrue(Equatable.AreEqual(this.DateTime, oa.DateTime));
            Assert.IsTrue(Equatable.AreEqual(this.TimeSpan, oa.TimeSpan));
            Assert.IsTrue(string.Equals(this.String, oa.String));
            Assert.IsTrue(string.Equals(this.StringBuilder != null ? this.StringBuilder.ToString() : null, oa.StringBuilder != null ? oa.StringBuilder.ToString() : null));
            Assert.IsTrue(Equatable.AreEqual(this.Guid, oa.Guid));
        }

        public void AssertIsEqualObjectA_Nullable(object o)
        {
            Assert.IsFalse(ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(ObjectA_Nullable));
            var oan = (ObjectA_Nullable)o;
            Assert.IsTrue(Equatable.AreEqual(this.Boolean, oan.Boolean));
            Assert.IsTrue(Equatable.AreEqual(this.SByte, oan.SByte));
            Assert.IsTrue(Equatable.AreEqual(this.Byte, oan.Byte));
            Assert.IsTrue(Equatable.AreEqual(this.Short, oan.Short));
            Assert.IsTrue(Equatable.AreEqual(this.UShort, oan.UShort));
            Assert.IsTrue(Equatable.AreEqual(this.Int, oan.Int));
            Assert.IsTrue(Equatable.AreEqual(this.UInt, oan.UInt));
            Assert.IsTrue(Equatable.AreEqual(this.Long, oan.Long));
            Assert.IsTrue(Equatable.AreEqual(this.ULong, oan.ULong));
            Assert.IsTrue(Equatable.AreEqual(this.Char, oan.Char));
            Assert.IsTrue(Equatable.AreEqual(this.Float, oan.Float));
            Assert.IsTrue(Equatable.AreEqual(this.Double, oan.Double));
            Assert.IsTrue(Equatable.AreEqual(this.Decimal, oan.Decimal));
            Assert.IsTrue(Equatable.AreEqual(this.DateTime, oan.DateTime));
            Assert.IsTrue(Equatable.AreEqual(this.TimeSpan, oan.TimeSpan));
            Assert.IsTrue(string.Equals(this.String, oan.String));
            Assert.IsTrue(string.Equals(this.StringBuilder != null ? this.StringBuilder.ToString() : null, oan.StringBuilder != null ? oan.StringBuilder.ToString() : null));
            Assert.IsTrue(Equatable.AreEqual(this.Guid, oan.Guid));
        }

        public ObjectA_Nullable Randomize()
        {
            this.Boolean = (Rng.Instance.Next() % 2) == 0;
            this.SByte = (sbyte)(Rng.Instance.Next(byte.MaxValue) - sbyte.MinValue);
            this.Byte = (byte)Rng.Instance.Next(byte.MaxValue);
            this.Short = (short)(Rng.Instance.Next(ushort.MaxValue) - short.MinValue);
            this.UShort = (ushort)Rng.Instance.Next(ushort.MaxValue);
            this.Int = Rng.Instance.Next(int.MaxValue) - int.MaxValue / 2;
            this.UInt = (uint)Rng.Instance.Next(int.MaxValue);
            this.Long = (long)(Rng.Instance.Next(int.MaxValue) - int.MaxValue / 2) * Rng.Instance.Next(byte.MaxValue);
            this.ULong = (ulong)Rng.Instance.Next(int.MaxValue) * (ulong)Rng.Instance.Next(byte.MaxValue);
            this.Char = (char)Rng.Instance.Next(byte.MaxValue);
            this.Float = (float)Rng.Instance.NextDouble();
            this.Double = Rng.Instance.NextDouble();
            this.Decimal = Rng.Instance.Next(int.MaxValue);
            this.DateTime = System.DateTime.UtcNow;
            this.TimeSpan = System.TimeSpan.FromTicks(Rng.Instance.Next(int.MaxValue));
            this.String = System.Guid.NewGuid().ToString();
            this.StringBuilder = new StringBuilder(System.Guid.NewGuid().ToString());
            this.Guid = System.Guid.NewGuid();

            return this;
        }
    }

    internal abstract class ObjectBase
    {
        protected ObjectBase()
        {
            this.Id = Guid.NewGuid().ToString();
        }

        public string Id { get; set; }
    }

    internal class ObjectBaseJ : ObjectBase
    {
        public ObjectA A { get; set; }
    }

    internal sealed class ObjectC
    {
        public bool Boolean { get; set; }

        public double[] DoubleArray { get; set; }

        public int GetOnly
        {
            get
            {
                return 0;
            }
        }

        public Guid Guid { get; set; }

        public int Int { get; set; }

        public ObjectA ObjectA { get; set; }

        public ObjectA[] ObjectAArray { get; set; }

        public ObjectA[,] ObjectAArray2 { get; set; }

        public ObjectA[][] ObjectAArray22 { get; set; }

        public ObjectA_Nullable ObjectAN { get; set; }

        public ObjectA_Nullable[] ObjectANArray { get; set; }

        public int SetOnly
        {
            set
            {
            }
        }

        public void AssertIsEqualObjectC(object o)
        {
            Assert.IsFalse(ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(ObjectC));
            var oc = (ObjectC)o;
            Assert.IsTrue(this.Boolean.Equals(oc.Boolean));
            Assert.IsTrue(this.Int.Equals(oc.Int));

            Assert.IsTrue((this.ObjectA == null) == (oc.ObjectA == null));
            if (this.ObjectA != null)
            {
                this.ObjectA.AssertIsEqualObjectA(oc.ObjectA);
            }

            Assert.IsTrue((this.ObjectAN == null) == (oc.ObjectAN == null));
            if (this.ObjectAN != null)
            {
                this.ObjectAN.AssertIsEqualObjectA_Nullable(oc.ObjectAN);
            }

            Assert.IsTrue((this.DoubleArray == null) == (oc.DoubleArray == null));
            if (this.DoubleArray != null)
            {
                Assert.IsTrue(Equatable.AreEqual((object)this.DoubleArray, oc.DoubleArray));
            }

            Assert.IsTrue((this.ObjectAArray == null) == (oc.ObjectAArray == null));
            if (this.ObjectAArray != null)
            {
                for (var n = 0; n < this.ObjectAArray.Length; ++n)
                {
                    Assert.IsTrue((this.ObjectAArray[n] == null) == (oc.ObjectAArray[n] == null));
                    if (this.ObjectAArray[n] != null)
                    {
                        this.ObjectAArray[n].AssertIsEqualObjectA(oc.ObjectAArray[n]);
                    }
                }
            }

            Assert.IsTrue((this.ObjectANArray == null) == (oc.ObjectANArray == null));
            if (this.ObjectAArray != null)
            {
                for (var n = 0; n < this.ObjectANArray.Length; ++n)
                {
                    Assert.IsTrue((this.ObjectANArray[n] == null) == (oc.ObjectANArray[n] == null));
                    if (this.ObjectANArray[n] != null)
                    {
                        this.ObjectANArray[n].AssertIsEqualObjectA_Nullable(oc.ObjectANArray[n]);
                    }
                }
            }

            Assert.IsTrue((this.ObjectAArray2 == null) == (oc.ObjectAArray2 == null));
            if (this.ObjectAArray2 != null)
            {
                for (var n = 0; n < 2; ++n)
                {
                    for (var m = 0; m < 2; ++m)
                    {
                        Assert.IsTrue((this.ObjectAArray2[n, m] == null) == (oc.ObjectAArray2[n, m] == null));
                        if (this.ObjectAArray2[n, m] != null)
                        {
                            this.ObjectAArray2[n, m].AssertIsEqualObjectA(oc.ObjectAArray2[n, m]);
                        }
                    }
                }
            }

            Assert.IsTrue((this.ObjectAArray22 == null) == (oc.ObjectAArray22 == null));
            if (this.ObjectAArray22 != null)
            {
                for (var n = 0; n < 2; ++n)
                {
                    for (var m = 0; m < 2; ++m)
                    {
                        Assert.IsTrue((this.ObjectAArray22[n][m] == null) == (oc.ObjectAArray22[n][m] == null));
                        if (this.ObjectAArray22[n][m] != null)
                        {
                            this.ObjectAArray22[n][m].AssertIsEqualObjectA(oc.ObjectAArray22[n][m]);
                        }
                    }
                }
            }
        }

        public ObjectC Randomize()
        {
            this.Boolean = (Rng.Instance.Next() % 2) == 0;
            this.Int = Rng.Instance.Next(int.MaxValue) - int.MaxValue / 2;
            this.Guid = Guid.NewGuid();
            this.ObjectA = new ObjectA().Randomize();
            this.ObjectAN = new ObjectA_Nullable().Randomize();
            this.DoubleArray = new double[5];
            for (var n = 0; n < this.DoubleArray.Length; ++n)
            {
                this.DoubleArray[n] = Rng.Instance.NextDouble();
            }

            this.ObjectAArray = new ObjectA[5];
            for (var n = 0; n < this.ObjectAArray.Length; ++n)
            {
                this.ObjectAArray[n] = new ObjectA().Randomize();
            }

            this.ObjectANArray = new ObjectA_Nullable[5];
            for (var n = 0; n < this.ObjectANArray.Length; ++n)
            {
                this.ObjectANArray[n] = new ObjectA_Nullable().Randomize();
            }

            this.ObjectAArray2 = new ObjectA[2, 2];
            for (var n = 0; n < 2; ++n)
            {
                for (var m = 0; m < 2; ++m)
                {
                    this.ObjectAArray2[n, m] = new ObjectA().Randomize();
                }
            }

            this.ObjectAArray22 = new ObjectA[2][];
            for (var n = 0; n < 2; ++n)
            {
                this.ObjectAArray22[n] = new ObjectA[2];
                for (var m = 0; m < 2; ++m)
                {
                    this.ObjectAArray22[n][m] = new ObjectA().Randomize();
                }
            }

            return this;
        }
    }

    internal sealed class ObjectE
    {
        public IfcA IfcA { get; set; }

        public IfcA[] IfcAArray { get; set; }

        public IfcB IfcB { get; set; }

        public IfcB[] IfcBArray { get; set; }

        public void AssertIsEqualObjectE(object o)
        {
            Assert.IsFalse(ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(ObjectE));
            var oe = (ObjectE)o;
            Assert.IsTrue((this.IfcA == null) == (oe.IfcA == null));
            if (this.IfcA != null)
            {
                this.IfcA.AssertIsEqualIfcA(oe.IfcA);
            }

            Assert.IsTrue((this.IfcB == null) == (oe.IfcB == null));
            if (this.IfcB != null)
            {
                this.IfcB.AssertIsEqualIfcB(oe.IfcB);
            }

            Assert.IsTrue((this.IfcAArray == null) == (oe.IfcAArray == null));
            if (this.IfcAArray != null)
            {
                for (var n = 0; n < this.IfcAArray.Length; ++n)
                {
                    Assert.IsTrue((this.IfcAArray[n] == null) == (oe.IfcAArray[n] == null));
                    if (this.IfcAArray[n] != null)
                    {
                        this.IfcAArray[n].AssertIsEqualIfcA(oe.IfcAArray[n]);
                    }
                }
            }

            Assert.IsTrue((this.IfcBArray == null) == (oe.IfcBArray == null));
            if (this.IfcBArray != null)
            {
                for (var n = 0; n < this.IfcBArray.Length; ++n)
                {
                    Assert.IsTrue((this.IfcBArray[n] == null) == (oe.IfcBArray[n] == null));
                    if (this.IfcBArray[n] != null)
                    {
                        this.IfcBArray[n].AssertIsEqualIfcB(oe.IfcBArray[n]);
                    }
                }
            }
        }

        public ObjectE Randomize()
        {
            this.IfcA = new IfcAImpl().Randomize();
            this.IfcB = new IfcBImpl().Randomize();
            this.IfcAArray = new IfcA[5];
            for (var n = 0; n < this.IfcAArray.Length; ++n)
            {
                this.IfcAArray[n] = new IfcAImpl().Randomize();
            }

            this.IfcBArray = new IfcB[5];
            for (var n = 0; n < this.IfcBArray.Length; ++n)
            {
                this.IfcBArray[n] = new IfcBImpl().Randomize();
            }

            return this;
        }
    }

    internal sealed class ObjectF
    {
        public byte[] ByteArray { get; set; }

        public long Long { get; set; }

        public ObjectG ObjectG { get; private set; }

        public void AssertIsEqualObjectF(object o)
        {
            Assert.IsFalse(ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(ObjectF));
            var of = (ObjectF)o;
            Assert.IsFalse(ReferenceEquals(this.ObjectG, of.ObjectG));
            Assert.IsTrue(this.Long.Equals(of.Long));
            Assert.IsFalse(ReferenceEquals(this.ByteArray, of.ByteArray));
            Assert.IsTrue(ReferenceEquals(of.ByteArray, of.ObjectG.ByteArray));
        }

        public ObjectF Randomize(ObjectG og)
        {
            this.ObjectG = og;
            this.Long = (long)(Rng.Instance.Next(int.MaxValue) - int.MaxValue / 2) * Rng.Instance.Next(byte.MaxValue);
            this.ByteArray = og.ByteArray;
            return this;
        }
    }

    internal sealed class ObjectG
    {
        public byte[] ByteArray { get; set; }

        public double Double { get; set; }

        public ObjectF ObjectF { get; private set; }

        public ObjectF[] ObjectFArray { get; private set; }

        public void AssertIsEqualObjectG(object o)
        {
            Assert.IsFalse(ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(ObjectG));
            var og = (ObjectG)o;
            Assert.IsTrue(this.Double.Equals(og.Double));
            Assert.IsTrue((this.ObjectF == null) == (og.ObjectF == null));
            if (this.ObjectF != null)
            {
                this.ObjectF.AssertIsEqualObjectF(og.ObjectF);
                Assert.IsTrue(ReferenceEquals(og, og.ObjectF.ObjectG));
                Assert.IsTrue(ReferenceEquals(og.ByteArray, og.ObjectF.ByteArray));
            }

            Assert.IsTrue((this.ObjectFArray == null) == (og.ObjectFArray == null));
            if (this.ObjectFArray != null)
            {
                for (var n = 0; n < this.ObjectFArray.Length; ++n)
                {
                    Assert.IsTrue((this.ObjectFArray[n] == null) == (og.ObjectFArray[n] == null));
                    if (this.ObjectFArray[n] != null)
                    {
                        this.ObjectFArray[n].AssertIsEqualObjectF(og.ObjectFArray[n]);
                        Assert.IsTrue(ReferenceEquals(og, og.ObjectFArray[n].ObjectG));
                        Assert.IsTrue(ReferenceEquals(og.ByteArray, og.ObjectFArray[n].ByteArray));
                    }
                }

                Assert.IsTrue(ReferenceEquals(this.ObjectFArray[0], this.ObjectFArray[3]));
                Assert.IsTrue(ReferenceEquals(this.ObjectFArray[1], this.ObjectFArray[2]));
                Assert.IsTrue(ReferenceEquals(og.ObjectFArray[0], og.ObjectFArray[3]));
                Assert.IsTrue(ReferenceEquals(og.ObjectFArray[1], og.ObjectFArray[2]));
            }
        }

        public ObjectG Randomize()
        {
            this.Double = Rng.Instance.NextDouble();
            this.ByteArray = new byte[5];
            for (var n = 0; n < this.ByteArray.Length; ++n)
            {
                this.ByteArray[n] = (byte)Rng.Instance.Next(byte.MaxValue);
            }

            this.ObjectF = new ObjectF().Randomize(this);
            this.ObjectFArray = new ObjectF[4];
            this.ObjectFArray[0] = new ObjectF().Randomize(this);
            this.ObjectFArray[1] = new ObjectF().Randomize(this);
            this.ObjectFArray[2] = this.ObjectFArray[1];
            this.ObjectFArray[3] = this.ObjectFArray[0];
            return this;
        }
    }

    internal sealed class ObjectH
    {
        public string String { get; set; }
    }

    internal sealed class ObjectH2
    {
    }

    internal sealed class ObjectI
    {
        public object[] Objects { get; set; }
    }

    internal sealed class ObjectJ : ObjectBaseJ
    {
        public ObjectC C { get; set; }
    }

    internal sealed class ObjectN
    {
        public ObjectN()
        {
        }

        public ObjectN(string name)
        {
            this.Name = name;
        }

        public string Name { get; set; }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            var objectN = obj as ObjectN;
            return objectN != null && string.Equals(this.Name, objectN.Name);
        }

        public override int GetHashCode()
        {
            return this.Name != null ? this.Name.GetHashCode() : 0;
        }
    }

    internal sealed class ObjectM
    {
        public object Item { get; set; }
    }

    [Serializable]
    internal sealed class ObjectSerializableA
    {
        public double X;

        public double Y;

        public double Z;

        public string Name { get; set; }

        public void AssertIsEqualObjectA_Nullable(object o)
        {
            Assert.IsFalse(ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(StructA_Nullable));
            var san = (StructA_Nullable)o;
            Assert.IsTrue(NullableEquals(this.X, san.X));
            Assert.IsTrue(NullableEquals(this.Y, san.Y));
            Assert.IsTrue(NullableEquals(this.Z, san.Z));
        }

        public void AssertIsEqualObjectSerializableA(object o)
        {
            Assert.IsFalse(ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(ObjectSerializableA));
            var sa = (ObjectSerializableA)o;
            Assert.AreEqual(this.X, sa.X);
            Assert.AreEqual(this.Y, sa.Y);
            Assert.AreEqual(this.Z, sa.Z);
            Assert.AreEqual(this.Name, sa.Name);
        }

        public void AssertIsEqualStructA(object o)
        {
            Assert.IsFalse(ReferenceEquals(this, o));
            Assert.IsNotNull(o);
            Assert.IsInstanceOfType(o, typeof(StructA));
            var sa = (StructA)o;
            Assert.AreEqual(this.X, sa.X);
            Assert.AreEqual(this.Y, sa.Y);
            Assert.AreEqual(this.Z, sa.Z);
        }

        private static bool NullableEquals<T>(T a, T? b) where T : struct
        {
            return (b.HasValue && a.Equals(b)) || (!b.HasValue && a.Equals(default(T)));
        }
    }

    [Serializable]
    internal sealed class ObjectSerializableAttributes : ObjectSerializableAttributesBase
    {
        [NonSerialized]
        public int OnDeserializing;

        [NonSerialized]
        public int OnSerializing;

        [OnDeserialized]
        internal void OnDeserializedMethod2(StreamingContext context)
        {
            Assert.IsNotNull(context);
            ++this.OnDeserialized;
        }

        [OnDeserializing]
        internal void OnDeserializingMethod(StreamingContext context)
        {
            Assert.IsNotNull(context);
            ++this.OnDeserializing;
        }

        [OnSerialized]
        internal void OnSerializedMethod2(StreamingContext context)
        {
            Assert.IsNotNull(context);
            ++this.OnSerialized;
        }

        [OnSerializing]
        internal void OnSerializingMethod(StreamingContext context)
        {
            Assert.IsNotNull(context);
            ++this.OnSerializing;
        }
    }

    [Serializable]
    internal class ObjectSerializableAttributesBase
    {
        [NonSerialized]
        public int OnDeserialized;

        [NonSerialized]
        public int OnSerialized;

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            Assert.IsNotNull(context);
            ++this.OnDeserialized;
        }

        [OnSerialized]
        internal void OnSerializedMethod(StreamingContext context)
        {
            Assert.IsNotNull(context);
            ++this.OnSerialized;
        }
    }
}
