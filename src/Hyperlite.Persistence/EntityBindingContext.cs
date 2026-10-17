namespace Hyperlite.Persistence
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hyperlite.Persistence.Bindings;
    using Hyperlite.Persistence.Collections;
    using Hyperlite.Persistence.Collections.Concurrent;
    using Hyperlite.Persistence.Reflection;

    internal class EntityBindingContext : BindingContext
    {
        private readonly ConcurrentTypeDictionary<EntityReference> entityReferences =
            new ConcurrentTypeDictionary<EntityReference>();

        internal EntityBindingContext(BindingContext bindingContext)
            : base(bindingContext)
        {
        }

        /// <summary>
        ///     Gets the column names for the entity reference specified.
        /// </summary>
        internal IEnumerable<string> ColumnNames(EntityReference entityReference)
        {
            var distinctColumnBindings = this.DistinctColumnBindingsForType(entityReference.EntityType).ToList();
            if (distinctColumnBindings.Count <= 1)
            {
                return distinctColumnBindings.Select(ColumnName);
            }

            var columnBindingComparer = new ColumnBindingComparer();
            var columnFamilyBindings = new StringDictionary<HashSet<IColumnBinding>>();

            foreach (var binding in distinctColumnBindings)
            {
                HashSet<IColumnBinding> columnBindings;
                if (!columnFamilyBindings.TryGetValue(binding.ColumnFamily, out columnBindings))
                {
                    columnFamilyBindings.Add(binding.ColumnFamily,
                        columnBindings = new HashSet<IColumnBinding>(columnBindingComparer));
                }

                columnBindings.Add(binding);
            }

            var registeredColumnNames = this.RegisteredColumnNames();

            foreach (var binding in distinctColumnBindings.Where(b => b.ColumnQualifier != null))
            {
                ISet<string> columnQualifiers;
                if (registeredColumnNames.TryGetValue(binding.ColumnFamily, out columnQualifiers))
                {
                    if (columnQualifiers.Remove(binding.ColumnQualifier) && columnQualifiers.Count == 0)
                    {
                        columnFamilyBindings[binding.ColumnFamily] =
                            new HashSet<IColumnBinding> { new ColumnBinding(binding.ColumnFamily) };
                    }
                }
            }

            return columnFamilyBindings.Values.SelectMany(s => s).Select(ColumnName);
        }

        internal EntityReference EntityReferenceForInspector(Inspector inspector)
        {
            return this.entityReferences.GetOrAdd(
                inspector.InspectedType,
                this,
                inspector,
                (t, _this, i) => _this.CreateEntityReference(i));
        }

        /// <param name="type">
        ///     The entity type.
        /// </param>
        internal EntityReference EntityReferenceForType(Type type)
        {
            return this.entityReferences.GetOrAdd(type, this, (t, _this) => _this.NewEntityReference(t));
        }

        /// <inheritdoc />
        protected override IEnumerable<Type> CommonBaseTypeCandidatesForType(Type type)
        {
            var candidates = base.CommonBaseTypeCandidatesForType(type);
            if (this.StrictExplicitColumnBinding || this.StrictExplicitTableBinding)
            {
                return candidates;
            }

            var candidate = TypeFinder.GetCommonBaseType(this.entityReferences.Keys.Where(type.IsAssignableFrom));
            return candidates.Concat(new[] { candidate });
        }

        /// <inheritdoc />
        protected override IEnumerable<IColumnBinding> DistinctColumnBindingsForType(Type type)
        {
            var bindings = base.DistinctColumnBindingsForType(type);
            if (this.StrictExplicitColumnBinding)
            {
                return bindings;
            }

            var mergedBindings = new HashSet<IColumnBinding>(bindings, new ColumnBindingComparer());
            foreach (var binding in this.entityReferences
                .Where(kv => type.IsAssignableFrom(kv.Key) && kv.Value.ColumnBinding != null)
                .Select(kv => kv.Value.ColumnBinding))
            {
                mergedBindings.Add(binding);
            }

            return mergedBindings;
        }

        /// <inheritdoc />
        protected override IEnumerable<ITableBinding> DistinctTableBindingsForType(Type type)
        {
            var bindings = base.DistinctTableBindingsForType(type);
            if (this.StrictExplicitTableBinding)
            {
                return bindings;
            }

            var mergedBindings = new HashSet<ITableBinding>(bindings, new TableBindingComparer());
            foreach (var binding in this.entityReferences.Where(kv => type.IsAssignableFrom(kv.Key))
                .Select(kv => kv.Value.TableBinding))
            {
                mergedBindings.Add(binding);
            }

            return mergedBindings;
        }

        /// <summary>
        ///     Removes all entity references affected by the column binding specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        protected override void RemovingColumnBinding(Type type, IColumnBinding columnBinding)
        {
            this.entityReferences.Remove(
                from kv in this.entityReferences
                where type.IsAssignableFrom(kv.Key) ||
                      (kv.Value != null && object.ReferenceEquals(kv.Value.ColumnBinding, columnBinding))
                select kv.Key);
        }

        /// <summary>
        ///     Removes all entity references affected by the key binding specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        protected override void RemovingKeyBinding(Type type, IKeyBinding keyBinding)
        {
            this.entityReferences.Remove(
                from kv in this.entityReferences
                where type.IsAssignableFrom(kv.Key) ||
                      (kv.Value != null && object.ReferenceEquals(kv.Value.KeyBinding, keyBinding))
                select kv.Key);
        }

        /// <summary>
        ///     Removes all entity references affected by the table binding specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        protected override void RemovingTableBinding(Type type, ITableBinding tableBinding)
        {
            this.entityReferences.Remove(
                from kv in this.entityReferences
                where type.IsAssignableFrom(kv.Key) ||
                      (kv.Value != null && object.ReferenceEquals(kv.Value.TableBinding, tableBinding))
                select kv.Key);
        }

        /// <summary>
        ///     Gets the fully qualified column name for the column binding specified.
        /// </summary>
        /// <returns>
        ///     The fully qualified column name.
        /// </returns>
        private static string ColumnName(IColumnBinding columnBinding)
        {
            return columnBinding.ColumnQualifier == null
                ? columnBinding.ColumnFamily
                : columnBinding.ColumnFamily + ":" + columnBinding.ColumnQualifier;
        }

        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <returns>
        ///     The newly created entity reference.
        /// </returns>
        private EntityReference NewEntityReference(Type type)
        {
            return this.CreateEntityReference(type);
        }
    }
}
