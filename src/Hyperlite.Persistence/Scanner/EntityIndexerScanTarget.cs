namespace Hyperlite.Persistence.Scanner
{
    using System;
    using System.Globalization;
    using Hyperlite.Persistence.Reflection;

    internal sealed class EntityIndexerScanTarget : EntityScanTarget
    {
        /// <summary>
        ///     The target collection.
        /// </summary>
        private readonly object collection;

        private readonly InspectedEnumerable inspectedEnumerable;

        /// <summary>
        ///     The position in the indexed collection.
        /// </summary>
        private readonly int pos;

        /// <param name="pos">
        ///     The position in the indexed collection.
        /// </param>
        /// <exception cref="PersistenceException">
        ///     If the <paramref name="inspectedEnumerable" /> does not have an indexer.
        /// </exception>
        internal EntityIndexerScanTarget(Type entityType, EntitySpec entitySpec,
            InspectedEnumerable inspectedEnumerable, object collection, int pos)
            : base(entityType, entitySpec)
        {
            if (!inspectedEnumerable.HasIndexer)
            {
                throw new PersistenceException(
                    string.Format(CultureInfo.InvariantCulture, @"Enumerable {0} has no index for type {1}.",
                        inspectedEnumerable.InspectedType, inspectedEnumerable.ElementType));
            }

            this.setter = this.Set;
            this.inspectedEnumerable = inspectedEnumerable;
            this.collection = collection;
            this.pos = pos;
        }

        /// <summary>
        ///     Sets the value at the target position.
        /// </summary>
        private void Set(object target, object value)
        {
            lock (this.inspectedEnumerable)
            {
                this.inspectedEnumerable.Indexer(this.collection, this.pos, value);
            }
        }
    }
}
