namespace Hyperlite.Persistence.Bindings
{
    using System;
    using Hyperlite.Persistence.Reflection;

    internal sealed class GuidPropertyKeyBinding : InspectedPropertyKeyBinding
    {
        private readonly Func<object, object> get;

        private readonly Action<object, object> set;

        internal GuidPropertyKeyBinding(InspectedProperty inspectedProperty, IColumnBinding columnBinding)
            : base(inspectedProperty, columnBinding)
        {
            this.get = inspectedProperty.Getter;
            this.set = inspectedProperty.Setter;
        }

        /// <inheritdoc />
        public override Key CreateKey(object entity)
        {
            var guid = Guid.NewGuid();
            this.set(entity, guid);
            return this.Merge(new Key(Encode(guid)));
        }

        /// <inheritdoc />
        public override Key KeyFromEntity(object entity)
        {
            return this.Merge(new Key(Encode((Guid)this.get(entity))));
        }

        /// <inheritdoc />
        public override Key KeyFromValue(object value)
        {
            return value is Guid ? this.Merge(new Key(Encode((Guid)value))) : base.KeyFromValue(value);
        }

        /// <inheritdoc />
        public override void SetKey(object entity, Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.set(entity, Decode(key.Row));
            this.Timestamp(entity, key);
        }

        /// <summary>
        ///     Decodes the given encoded string GUID into a <see cref="Guid" /> instance.
        /// </summary>
        /// <param name="value">
        ///     The encoded string GUID.
        /// </param>
        /// <returns>
        ///     The decoded guid instance.
        /// </returns>
        private static Guid Decode(string value)
        {
            return Key.Decode(value);
        }

        /// <summary>
        ///     Encodes the given <see cref="Guid" /> instance into a string GUID.
        /// </summary>
        /// <returns>
        ///     The encoded string GUID.
        /// </returns>
        private static string Encode(Guid value)
        {
            return Key.Encode(value);
        }
    }
}
