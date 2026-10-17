namespace Hyperlite.Persistence.Scanner
{
    using System;
    using System.Globalization;
    using Hyperlite.Persistence.Reflection;

    internal sealed class EntityCollectionScanTarget : EntityScanTarget
    {
        /// <summary>
        ///     The collection add action.
        /// </summary>
        private readonly Action<object, object> add;

        /// <summary>
        ///     The target collection.
        /// </summary>
        private readonly object collection;

        /// <param name="entitySpec">
        ///     The entity specification.
        /// </param>
        /// <param name="collection">
        ///     The target collection.
        /// </param>
        /// <exception cref="PersistenceException">
        ///     If the <paramref name="inspectedEnumerable" /> does not have an add method.
        /// </exception>
        internal EntityCollectionScanTarget(Type entityType, EntitySpec entitySpec,
            InspectedEnumerable inspectedEnumerable, object collection)
            : base(entityType, entitySpec)
        {
            if (!inspectedEnumerable.HasAdd)
            {
                throw new PersistenceException(
                    string.Format(CultureInfo.InvariantCulture, @"Enumerable {0} has no void Add({1}) method.",
                        inspectedEnumerable.InspectedType, inspectedEnumerable.ElementType));
            }

            this.setter = this.Add;
            this.add = inspectedEnumerable.Add;
            this.collection = collection;
        }

        /// <summary>
        ///     Adds a value to the target collection.
        /// </summary>
        private void Add(object target, object value)
        {
            lock (this.collection)
            {
                this.add(this.collection, value);
            }
        }
    }
}
