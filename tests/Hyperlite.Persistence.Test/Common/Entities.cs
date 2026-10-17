namespace Hyperlite.Persistence.Test.Common
{
    using System;
    using System.Runtime.Serialization;

    using Hyperlite.Persistence.Attributes;

    internal sealed class EntityA
    {
        public EntityA()
        {
            this.Name = Guid.NewGuid().ToString();
        }

        public Guid Id { get; private set; }

        [DataMember(Name = "PropName")]
        public string Name { get; set; }

        public override bool Equals(object o)
        {
            if (object.ReferenceEquals(this, o))
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

    internal sealed class EntityB
    {
        public EntityB()
        {
            this.Name = Guid.NewGuid().ToString();
        }

        public Guid Id { get; private set; }

        public string Name { get; set; }

        public override bool Equals(object o)
        {
            if (object.ReferenceEquals(this, o))
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

    [Entity("TestEntityManager")]
    internal abstract class EntityBase
    {
        public string Id { get; private set; }

        public DateTime LastModified { get; private set; }
    }

    [Entity(ColumnFamily = "a")]
    internal abstract class EntityXBase : EntityBase
    {
    }

    [Entity(ColumnQualifier = "1")]
    internal sealed class EntityX : EntityXBase
    {
        public EntityX()
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

    [Entity(ColumnQualifier = "2")]
    internal sealed class EntityX2 : EntityXBase
    {
        public EntityX2()
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

            if (!(o is EntityX2))
            {
                return false;
            }

            return string.Equals(this.Name, (o as EntityX2).Name);
        }

        public override int GetHashCode()
        {
            return this.Name.GetHashCode();
        }
    }

    [Entity(ColumnFamily = "b")]
    internal sealed class EntityY : EntityBase
    {
        public EntityY()
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

    [Entity(ColumnFamily = "c")]
    internal class EntityC : EntityBase
    {
        public EntityC()
        {
            this.Name = Guid.NewGuid().ToString();
        }

        public EntityA A { get; set; }

        public EntityB B { get; set; }

        public string Name { get; set; }

        public EntityX X { get; set; }

        public EntityY Y { get; set; }

        public override bool Equals(object o)
        {
            if (object.ReferenceEquals(this, o))
            {
                return true;
            }

            if (!(o is EntityC))
            {
                return false;
            }

            var c = o as EntityC;
            return string.Equals(this.Name, c.Name) && (this.A == null ? c.A == null : this.A.Equals(c.A)) && (this.B == null ? c.B == null : this.B.Equals(c.B))
                   && (this.X == null ? c.X == null : this.X.Equals(c.X)) && (this.Y == null ? c.Y == null : this.Y.Equals(c.Y));
        }

        public override int GetHashCode()
        {
            return this.Name.GetHashCode();
        }
    }
}
