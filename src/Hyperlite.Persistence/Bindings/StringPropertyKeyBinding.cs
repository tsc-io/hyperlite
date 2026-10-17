namespace Hyperlite.Persistence.Bindings
{
    using System;
    using Hyperlite.Persistence.Reflection;

    internal sealed class StringPropertyKeyBinding : InspectedPropertyKeyBinding
    {
        private readonly Func<object, object> get;

        private readonly Action<object, object> set;

        internal StringPropertyKeyBinding(InspectedProperty inspectedProperty, IColumnBinding columnBinding)
            : base(inspectedProperty, columnBinding)
        {
            this.get = inspectedProperty.Getter;
            this.set = inspectedProperty.Setter;
        }

        /// <inheritdoc />
        public override Key CreateKey(object entity)
        {
            var key = this.GenerateKey(new Key(), entity.GetType());
            this.set(entity, key.Row);
            return key;
        }

        /// <inheritdoc />
        public override Key KeyFromEntity(object entity)
        {
            return this.Merge(new Key((string)this.get(entity)));
        }

        /// <inheritdoc />
        public override Key KeyFromValue(object value)
        {
            var key = value as string;
            return key != null ? this.Merge(new Key(key)) : base.KeyFromValue(value);
        }

        /// <inheritdoc />
        public override void SetKey(object entity, Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.set(entity, key.Row);
            this.Timestamp(entity, key);
        }
    }
}
