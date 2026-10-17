namespace Hyperlite.Persistence.Bindings
{
    using System;
    using Hyperlite.Persistence.Attributes;
    using Hyperlite.Persistence.Extensions;

    internal sealed class DefaultTableBinding : ITableBinding
    {
        private readonly Type initialType;

        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="type" /> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     If <paramref name="type" /> is typeof(object).
        /// </exception>
        internal DefaultTableBinding(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (type == typeof(object))
            {
                throw new ArgumentException(@"typeof(object) is not a valid table binding type", nameof(type));
            }

            this.initialType = type;
            this.Merge(type);
        }

        /// <summary>
        ///     Gets a value indicating whether the binding is complete.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the binding is complete, otherwise <c>false</c>.
        /// </value>
        public bool IsComplete => !string.IsNullOrEmpty(this.TableName) && !string.IsNullOrEmpty(this.Namespace);

        /// <inheritdoc />
        public string Namespace { get; private set; }

        /// <value>
        ///     The database table name.
        /// </value>
        public string TableName { get; private set; }

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
                    if (string.IsNullOrEmpty(this.Namespace))
                    {
                        this.Namespace = entityAttribute.Namespace;
                    }

                    if (string.IsNullOrEmpty(this.TableName))
                    {
                        this.TableName = entityAttribute.TableName;
                    }
                }
            }
            else if (string.IsNullOrEmpty(this.TableName))
            {
                this.TableName = this.initialType.Name;
            }
        }
    }
}
