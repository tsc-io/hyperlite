namespace Hyperlite.Persistence.Scanner
{
    using System;

    internal sealed class EntityQueryScanTarget : EntityScanTarget
    {
        private readonly Action<object> valueSink;

        /// <param name="key">
        ///     The entity key.
        /// </param>
        internal EntityQueryScanTarget(Type entityType, EntitySpec entitySpec, Key key, Action<object> valueSink)
            : base(entityType, entitySpec, key)
        {
            this.setter = this.Add;
            this.valueSink = valueSink;
        }

        /// <summary>
        ///     Adds a value to the target collection.
        /// </summary>
        private void Add(object target, object value)
        {
            this.valueSink(value);
        }
    }
}
