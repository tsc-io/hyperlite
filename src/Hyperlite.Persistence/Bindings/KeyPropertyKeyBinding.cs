namespace Hyperlite.Persistence.Bindings
{
    using System;
    using Hyperlite.Persistence.Reflection;

    internal sealed class KeyPropertyKeyBinding : InspectedPropertyKeyBinding
    {
        private readonly Func<object, object> get;

        private readonly Action<object, object> set;

        /// <exception cref="ArgumentException">
        ///     If the <paramref name="inspectedProperty" /> does not have an entity key.
        /// </exception>
        internal KeyPropertyKeyBinding(InspectedProperty inspectedProperty, IColumnBinding columnBinding)
            : base(inspectedProperty, columnBinding)
        {
            if (!inspectedProperty.HasKey)
            {
                throw new ArgumentException("Invalid property, missing entity key", nameof(inspectedProperty));
            }

            this.get = inspectedProperty.Getter;
            this.set = inspectedProperty.Setter;
        }

        /// <inheritdoc />
        public override Key CreateKey(object entity)
        {
            Key key;
            var obj = this.get(entity);
            if (obj == null)
            {
                key = new Key();
                this.set(entity, key);
            }
            else
            {
                key = (Key)obj;
            }

            return this.GenerateKey(key, entity.GetType());
        }

        /// <inheritdoc />
        public override Key KeyFromEntity(object entity)
        {
            return this.get(entity) as Key;
        }

        /// <inheritdoc />
        public override void SetKey(object entity, Key key)
        {
            this.set(entity, key);
        }
    }
}
