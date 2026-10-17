namespace Hyperlite.Persistence.Bindings
{
    using System;
    using System.Globalization;
    using Hyperlite.Persistence.Attributes;
    using Hyperlite.Persistence.Extensions;

    internal sealed class DefaultColumnBinding : IColumnBinding
    {
        private readonly string defaultColumnFamily;

        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If the <paramref name="type" /> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     If the <paramref name="type" /> is typeof(object).
        /// </exception>
        private DefaultColumnBinding(Type type, string defaultColumnFamily)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (type == typeof(object))
            {
                throw new ArgumentException(@"typeof(object) is not a valid column binding type", nameof(type));
            }

            if (type.IsInterface)
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture,
                        @"interface type {0} is not a valid column binding type", type), nameof(type));
            }

            this.defaultColumnFamily = defaultColumnFamily;

            var entityAttribute = ReflectionExtensions.GetAttribute<EntityAttribute>(type);
            if (entityAttribute != null)
            {
                this.ColumnFamily = entityAttribute.ColumnFamily;
                this.ColumnQualifier = entityAttribute.ColumnQualifier;
            }

            if (type.IsAbstract)
            {
                if (string.IsNullOrEmpty(this.ColumnFamily))
                {
                    throw new ArgumentException(
                        string.Format(CultureInfo.InvariantCulture,
                            @"abstract type {0} requires a column family binding", type), nameof(type));
                }

                if (!string.IsNullOrEmpty(this.ColumnQualifier))
                {
                    throw new ArgumentException(
                        string.Format(CultureInfo.InvariantCulture,
                            @"abstract type {0} may not bind to a column qualifier", type), nameof(type));
                }
            }
        }

        public string ColumnFamily { get; private set; }

        public string ColumnQualifier { get; }

        /// <summary>
        ///     Gets a value indicating whether the binding is complete.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the binding is complete, otherwise <c>false</c>.
        /// </value>
        public bool IsComplete => !string.IsNullOrEmpty(this.ColumnFamily);

        /// <summary>
        ///     Creates a new instance of the <see cref="DefaultColumnBinding" /> class.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If the <paramref name="type" /> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     If the <paramref name="type" /> is typeof(object).
        /// </exception>
        internal static DefaultColumnBinding Create(Type type, string defaultColumnFamily)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (type == typeof(object))
            {
                return null;
            }

            if (type.IsAbstract)
            {
                var entityAttribute = ReflectionExtensions.GetAttribute<EntityAttribute>(type);
                if (string.IsNullOrEmpty(entityAttribute?.ColumnFamily))
                {
                    return null;
                }
            }

            return new DefaultColumnBinding(type, defaultColumnFamily);
        }

        /// <summary>
        ///     Merge in binding elements from the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        internal void Merge(Type type)
        {
            if (type != null && type != typeof(object))
            {
                var entityAttribute = ReflectionExtensions.GetAttribute<EntityAttribute>(type);
                if (entityAttribute != null)
                {
                    if (string.IsNullOrEmpty(this.ColumnFamily))
                    {
                        this.ColumnFamily = entityAttribute.ColumnFamily;
                    }
                }
            }
            else if (string.IsNullOrEmpty(this.ColumnFamily))
            {
                if (string.IsNullOrEmpty(this.defaultColumnFamily))
                {
                    throw new InvalidOperationException("Undefined default column family");
                }

                this.ColumnFamily = this.defaultColumnFamily;
            }
        }
    }
}
