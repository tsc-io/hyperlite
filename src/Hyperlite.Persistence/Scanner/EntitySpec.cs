namespace Hyperlite.Persistence.Scanner
{
    using System;
    using System.Collections.Generic;

    internal class EntitySpec : IEquatable<EntitySpec>
    {
        private static readonly IEqualityComparer<Key> KeyComparer = PersistenceConfiguration.UseUnqualifiedKeys ? (IEqualityComparer<Key>)new RowComparer() : new KeyComparer();

        /// <summary>
        ///     Not part of equatable and hash.
        /// </summary>
        protected readonly Type entityType;

        /// <summary>
        ///     The pre-calculated hash code.
        /// </summary>
        private readonly int hashCode;

        /// <summary>
        ///     The entity key.
        /// </summary>
        private readonly Key key;

        /// <summary>
        ///     The database namespace.
        /// </summary>
        private readonly string ns;

        private readonly string tableName;

        /// <param name="ns">
        ///     The database namespace.
        /// </param>
        /// <param name="tableName">
        ///     The database table name.
        /// </param>
        /// <param name="key">
        ///     The entity key.
        /// </param>
        internal EntitySpec(Type entityType, string ns, string tableName, Key key)
        {
            this.entityType = entityType;
            this.ns = ns;
            this.tableName = tableName;
            this.key = key;

            unchecked
            {
                this.hashCode = 17;
                if (this.key != null)
                {
                    this.hashCode = (29 * this.hashCode) + KeyComparer.GetHashCode(this.key);
                }

                this.hashCode = (31 * this.hashCode) + this.tableName.GetHashCode();
                this.hashCode = (37 * this.hashCode) + (this.ns ?? string.Empty).GetHashCode();
            }
        }

        /// <param name="key">
        ///     The entity key.
        /// </param>
        internal EntitySpec(EntityReference entityReference, Key key)
            : this(entityReference.EntityType, entityReference.Namespace, entityReference.TableName, key)
        {
        }

        /// <param name="other">
        ///     The other entity specification.
        /// </param>
        internal EntitySpec(EntitySpec other)
        {
            this.entityType = other.entityType;
            this.ns = other.ns;
            this.tableName = other.tableName;
            this.key = other.key;
            this.hashCode = other.hashCode;
        }

        /// <param name="other">
        ///     The other entity specification.
        /// </param>
        protected EntitySpec(Type entityType, EntitySpec other)
            : this(entityType, other.ns, other.tableName, other.Key)
        {
        }

        /// <param name="other">
        ///     The other entity specification.
        /// </param>
        /// <param name="key">
        ///     The entity key.
        /// </param>
        protected EntitySpec(Type entityType, EntitySpec other, Key key)
            : this(entityType, other.ns, other.tableName, key)
        {
        }

        internal Type EntityType => this.entityType;

        /// <summary>
        ///     Gets the entity key.
        /// </summary>
        /// <value>
        ///     The entity  key.
        /// </value>
        internal Key Key => this.key;

        /// <value>
        ///     The database namespace.
        /// </value>
        internal string Namespace => this.ns;

        /// <value>
        ///     The database table name.
        /// </value>
        internal string TableName => this.tableName;

        /// <summary>
        ///     Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">
        ///     An object to compare with this object.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the current object is equal to the <paramref name="other" /> parameter, otherwise <c>false</c>.
        /// </returns>
        public bool Equals(EntitySpec other)
        {
            if (other == null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (!KeyComparer.Equals(this.key, other.key))
            {
                return false;
            }

            return string.Compare(this.tableName, other.tableName, StringComparison.Ordinal) == 0
                   && string.Compare(this.ns ?? string.Empty, other.ns ?? string.Empty, StringComparison.Ordinal) == 0;
        }

        /// <summary>
        ///     Indicates whether the current object is equal to another object.
        /// </summary>
        /// <param name="other">
        ///     An object to compare with this object.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the current object is equal to the <paramref name="other" /> parameter, otherwise <c>false</c>.
        /// </returns>
        public override bool Equals(object other)
        {
            return this.Equals(other as EntitySpec);
        }

        /// <returns>
        ///     A hash code for the current <see cref="EntitySpec" />.
        /// </returns>
        public override int GetHashCode()
        {
            return this.hashCode;
        }
    }
}
