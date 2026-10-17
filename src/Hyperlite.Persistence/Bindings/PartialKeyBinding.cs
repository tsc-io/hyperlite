namespace Hyperlite.Persistence.Bindings
{
    using System;
    using System.Globalization;

    public abstract class PartialKeyBinding : IKeyBinding
    {
        private readonly Type entityType;

        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="entityType" /> is null.
        /// </exception>
        protected PartialKeyBinding(Type entityType)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            this.entityType = entityType;
        }

        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="entityType" /> is null.
        /// </exception>
        protected PartialKeyBinding(Type entityType, IColumnBinding columnBinding)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            this.entityType = entityType;
            this.ColumnBinding = columnBinding;
        }

        public Type EntityType => this.entityType;

        internal IColumnBinding ColumnBinding { get; set; }

        internal Action<object, object> TimestampAction { get; set; }

        /// <inheritdoc />
        public abstract Key CreateKey(object entity);

        /// <inheritdoc />
        public abstract Key KeyFromEntity(object entity);

        /// <inheritdoc />
        public virtual Key KeyFromValue(object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var key = value as Key;
            if (key != null)
            {
                return key;
            }

            if (this.entityType.IsInstanceOfType(value))
            {
                return this.KeyFromEntity(value);
            }

            return this.Merge(new Key((string)Convert.ChangeType(value, typeof(string), CultureInfo.CurrentCulture)));
        }

        /// <inheritdoc />
        public abstract void SetKey(object entity, Key key);

        /// <summary>
        ///     Generates a new key.
        /// </summary>
        /// <param name="key">
        ///     The key to update.
        /// </param>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <returns>
        ///     The updated key.
        /// </returns>
        protected Key GenerateKey(Key key, Type type)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            key.Row = Key.Generate(type);
            return this.Merge(key);
        }

        /// <summary>
        ///     Merge in the column details.
        /// </summary>
        /// <param name="key">
        ///     The to merge in key the column details.
        /// </param>
        /// <returns>
        ///     The merged key.
        /// </returns>
        protected Key Merge(Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var columnBinding = this.ColumnBinding;
            if (columnBinding != null)
            {
                key.ColumnFamily = columnBinding.ColumnFamily;
                key.ColumnQualifier = columnBinding.ColumnQualifier;
            }

            return key;
        }

        /// <summary>
        ///     Executes the timestamp action for the entity and database key specified.
        /// </summary>
        /// <param name="key">
        ///     The database key.
        /// </param>
        protected void Timestamp(object entity, Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.TimestampAction?.Invoke(entity, key.DateTime);
        }
    }
}
