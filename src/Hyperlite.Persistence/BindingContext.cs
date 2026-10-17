namespace Hyperlite.Persistence
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Hyperlite.Persistence.Bindings;
    using Hyperlite.Persistence.Collections.Concurrent;
    using Hyperlite.Persistence.Reflection;

    public class BindingContext
    {
        /// <summary>
        ///     The default regular expression for the id property.
        /// </summary>
        private static readonly Regex DefaultIdPropertyName = new Regex("^([Ii]d$)|(<Id>.*)", RegexOptions.Compiled);

        /// <summary>
        ///     The default regular expression for the timestamp property.
        /// </summary>
        private static readonly Regex DefaultTimestampPropertyName =
            new Regex("^([Ll]astModified$)|(<LastModified>.*)", RegexOptions.Compiled);

        private readonly ConcurrentTypeDictionary<BindingSpec<IColumnBinding>> columnBindings =
            new ConcurrentTypeDictionary<BindingSpec<IColumnBinding>>();

        private readonly ConcurrentTypeDictionary<IKeyBinding> keyBindings = new ConcurrentTypeDictionary<IKeyBinding>()
            ;

        private readonly ConcurrentTypeDictionary<EntityReference> preliminaryEntityReferences;

        private readonly object syncRoot = new object();

        private readonly ConcurrentTypeDictionary<BindingSpec<ITableBinding>> tableBindings =
            new ConcurrentTypeDictionary<BindingSpec<ITableBinding>>();

        private string defaultColumnFamily;

        private Regex idPropertyName;

        private Lazy<ConcurrentTypeDictionary<IColumnBinding>> registeredColumnBindings;

        private Lazy<IDictionary<string, ISet<string>>> registeredColumnNames;

        private Lazy<ConcurrentTypeDictionary<ITableBinding>> registeredTableBindings;

        private bool strictExplicitColumnBinding;

        private bool strictExplicitKeyBinding;

        private bool strictExplicitTableBinding;

        private bool strictStaticColumnBinding;

        private bool strictStaticTableBinding;

        private Regex timestampPropertyName;

        public BindingContext()
        {
            this.preliminaryEntityReferences = new ConcurrentTypeDictionary<EntityReference>();

            this.DefaultColumnFamily = "e";
            this.IdPropertyName = DefaultIdPropertyName;
            this.TimestampPropertyName = DefaultTimestampPropertyName;

            this.Refresh();
        }

        /// <exception cref="ArgumentNullException">
        ///     The type of <paramref name="bindingContext" /> is null.
        /// </exception>
        public BindingContext(BindingContext bindingContext)
        {
            if (bindingContext == null)
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            this.preliminaryEntityReferences = bindingContext.preliminaryEntityReferences;

            this.DefaultColumnFamily = bindingContext.DefaultColumnFamily;
            this.IdPropertyName = bindingContext.IdPropertyName != null
                ? new Regex(bindingContext.IdPropertyName.ToString(), RegexOptions.Compiled)
                : null;
            this.TimestampPropertyName = bindingContext.TimestampPropertyName != null
                ? new Regex(bindingContext.TimestampPropertyName.ToString(), RegexOptions.Compiled)
                : null;

            this.StrictExplicitColumnBinding = bindingContext.StrictExplicitColumnBinding;
            this.StrictExplicitTableBinding = bindingContext.StrictExplicitTableBinding;
            this.StrictExplicitKeyBinding = bindingContext.StrictExplicitKeyBinding;
            this.StrictStaticTableBinding = bindingContext.StrictStaticTableBinding;
            this.StrictStaticColumnBinding = bindingContext.StrictStaticColumnBinding;

            this.columnBindings =
                new ConcurrentTypeDictionary<BindingSpec<IColumnBinding>>(bindingContext.columnBindings);
            this.keyBindings = new ConcurrentTypeDictionary<IKeyBinding>(bindingContext.keyBindings);
            this.tableBindings = new ConcurrentTypeDictionary<BindingSpec<ITableBinding>>(bindingContext.tableBindings);

            this.Refresh();
        }

        public string DefaultColumnFamily
        {
            get { return this.defaultColumnFamily; }
            set
            {
                this.defaultColumnFamily = value;
                this.Refresh();
            }
        }

        /// <summary>
        ///     Gets or sets identifier property name regular expression.
        /// </summary>
        /// <value>
        ///     The identifier property name.
        /// </value>
        public Regex IdPropertyName
        {
            get { return this.idPropertyName; }
            set
            {
                this.idPropertyName = value;
                this.Refresh();
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether strict explicit column binding has been enabled.
        /// </summary>
        public bool StrictExplicitColumnBinding
        {
            get { return this.strictExplicitColumnBinding; }
            set
            {
                this.strictExplicitColumnBinding = value;
                this.Refresh();
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether strict explicit key binding has been enabled.
        /// </summary>
        public bool StrictExplicitKeyBinding
        {
            get { return this.strictExplicitKeyBinding; }
            set
            {
                this.strictExplicitKeyBinding = value;
                this.Refresh();
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether strict explicit table binding has been enabled.
        /// </summary>
        public bool StrictExplicitTableBinding
        {
            get { return this.strictExplicitTableBinding; }
            set
            {
                this.strictExplicitTableBinding = value;
                this.Refresh();
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether strict static column binding has been enabled.
        /// </summary>
        public bool StrictStaticColumnBinding
        {
            get { return this.strictStaticColumnBinding; }
            set
            {
                this.strictStaticColumnBinding = value;
                this.Refresh();
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether strict static table binding has been enabled.
        /// </summary>
        public bool StrictStaticTableBinding
        {
            get { return this.strictStaticTableBinding; }
            set
            {
                this.strictStaticTableBinding = value;
                this.Refresh();
            }
        }

        /// <summary>
        ///     Gets or sets timestamp property name regular expression.
        /// </summary>
        public Regex TimestampPropertyName
        {
            get { return this.timestampPropertyName; }
            set
            {
                this.timestampPropertyName = value;
                this.Refresh();
            }
        }

        /// <summary>
        ///     Determines whether the binding context contains a column binding for the specified type.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the binding context contains a column binding for the specified type; otherwise, <c>false</c>.
        /// </returns>
        public bool ContainsColumnBinding(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return this.columnBindings.ContainsKey(type);
        }

        /// <summary>
        ///     Determines whether the binding context contains a key binding for the specified type.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the binding context contains a key binding for the specified type; otherwise, <c>false</c>.
        /// </returns>
        public bool ContainsKeyBinding(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return this.keyBindings.ContainsKey(type);
        }

        /// <summary>
        ///     Determines whether the binding context contains a table binding for the specified type.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the binding context contains a table binding for the specified type; otherwise, <c>false</c>.
        /// </returns>
        public bool ContainsTableBinding(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return this.tableBindings.ContainsKey(type);
        }

        /// <summary>
        ///     Registers a column binding for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the column binding has been added, <c>false</c> if an existing column binding has been replaced.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="type" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="columnBinding" /> is null.
        /// </exception>
        public bool RegisterColumnBinding(Type type, IColumnBinding columnBinding)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (columnBinding == null)
            {
                throw new ArgumentNullException(nameof(columnBinding));
            }

            // a type the application binds is a type it declares, so strict types read it
            Serialization.Encoder.Configuration.TypeAllowList.Declare(type);

            lock (this.syncRoot)
            {
                this.Refresh();

                BindingSpec<IColumnBinding> existingColumnBinding;
                if (this.columnBindings.TryGetValue(type, out existingColumnBinding))
                {
                    this.RemovingColumnBinding(type, existingColumnBinding.Binding);
                }

                this.columnBindings.Remove(from kv in this.columnBindings
                                           where kv.Value.Derived && type.IsAssignableFrom(kv.Key)
                                           select kv.Key);
                return this.columnBindings.AddOrUpdate(type, new BindingSpec<IColumnBinding>(columnBinding));
            }
        }

        /// <summary>
        ///     Registers a key binding for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the key binding has been added, <c>false</c> if an existing key binding has been replaced.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="type" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="keyBinding" /> is null.
        /// </exception>
        public bool RegisterKeyBinding(Type type, IKeyBinding keyBinding)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (keyBinding == null)
            {
                throw new ArgumentNullException(nameof(keyBinding));
            }

            // a type the application binds is a type it declares, so strict types read it
            Serialization.Encoder.Configuration.TypeAllowList.Declare(type);

            lock (this.syncRoot)
            {
                this.Refresh();

                IKeyBinding existingKeyBinding;
                if (this.keyBindings.TryGetValue(type, out existingKeyBinding))
                {
                    this.RemovingKeyBinding(type, existingKeyBinding);
                }

                return this.keyBindings.AddOrUpdate(type, keyBinding);
            }
        }

        /// <summary>
        ///     Registers a table binding for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <param name="tableBinding">
        ///     The key binding.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the table binding has been added, <c>false</c> if an existing table binding has been replaced.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="type" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="tableBinding" /> is null.
        /// </exception>
        public bool RegisterTableBinding(Type type, ITableBinding tableBinding)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (tableBinding == null)
            {
                throw new ArgumentNullException(nameof(tableBinding));
            }

            // a type the application binds is a type it declares, so strict types read it
            Serialization.Encoder.Configuration.TypeAllowList.Declare(type);

            lock (this.syncRoot)
            {
                this.Refresh();

                BindingSpec<ITableBinding> existingTableBinding;
                if (this.tableBindings.TryGetValue(type, out existingTableBinding))
                {
                    this.RemovingTableBinding(type, existingTableBinding.Binding);
                }

                this.tableBindings.Remove(from kv in this.tableBindings
                                          where kv.Value.Derived && type.IsAssignableFrom(kv.Key)
                                          select kv.Key);
                return this.tableBindings.AddOrUpdate(type, new BindingSpec<ITableBinding>(tableBinding));
            }
        }

        /// <summary>
        ///     Gets the column binding associated with the specified type.
        /// </summary>
        /// <param name="columnBinding">
        ///     When this method returns, contains the column binding associated with the specified type, if the type is found;
        ///     otherwise null.
        /// </param>
        /// <returns>
        ///     <c>true</c> if a column binding exists for the type specified; otherwise, <c>false</c>.
        /// </returns>
        public bool TryGetColumnBinding(Type type, out IColumnBinding columnBinding)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (this.syncRoot)
            {
                columnBinding = this.GetColumnBindingForType(type);
            }

            return columnBinding != null;
        }

        /// <summary>
        ///     Gets the key binding associated with the specified type.
        /// </summary>
        /// <param name="keyBinding">
        ///     When this method returns, contains the key binding associated with the specified type, if the type is found;
        ///     otherwise null.
        /// </param>
        /// <returns>
        ///     <c>true</c> if a key binding exists for the type specified; otherwise, <c>false</c>.
        /// </returns>
        public bool TryGetKeyBinding(Type type, out IKeyBinding keyBinding)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (this.syncRoot)
            {
                var columnBinding = this.GetColumnBindingForType(type);
                keyBinding = this.GetKeyBindingForType(type, columnBinding);
            }

            return keyBinding != null;
        }

        /// <summary>
        ///     Gets the table binding associated with the specified type.
        /// </summary>
        /// <param name="tableBinding">
        ///     When this method returns, contains the table binding associated with the specified type, if the type is found;
        ///     otherwise null.
        /// </param>
        /// <returns>
        ///     <c>true</c> if a table binding exists for the type specified; otherwise, <c>false</c>.
        /// </returns>
        public bool TryGetTableBinding(Type type, out ITableBinding tableBinding)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (this.syncRoot)
            {
                tableBinding = this.GetTableBindingForType(type);
            }

            return tableBinding != null;
        }

        /// <summary>
        ///     Unregister any registered bindings for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        public void UnregisterBinding(Type type)
        {
            this.UnregisterTableBinding(type);
            this.UnregisterKeyBinding(type);
            this.UnregisterColumnBinding(type);
        }

        /// <summary>
        ///     Unregister the column binding for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <returns>
        ///     <c>true</c> if an existing binding has been removed, otherwise <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="type" /> is null.
        /// </exception>
        public bool UnregisterColumnBinding(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (this.syncRoot)
            {
                this.Refresh();

                BindingSpec<IColumnBinding> columnBinding;
                if (this.columnBindings.TryGetValue(type, out columnBinding))
                {
                    this.RemovingColumnBinding(type, columnBinding.Binding);
                }

                this.columnBindings.Remove(from kv in this.columnBindings
                                           where kv.Value.Derived && type.IsAssignableFrom(kv.Key)
                                           select kv.Key);
                return this.columnBindings.Remove(type);
            }
        }

        /// <summary>
        ///     Unregister the key binding for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <returns>
        ///     <c>true</c> if an existing binding has been removed, otherwise <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="type" /> is null.
        /// </exception>
        public bool UnregisterKeyBinding(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (this.syncRoot)
            {
                this.Refresh();

                IKeyBinding keyBinding;
                if (this.keyBindings.TryGetValue(type, out keyBinding))
                {
                    this.RemovingKeyBinding(type, keyBinding);
                }

                return this.keyBindings.Remove(type);
            }
        }

        /// <summary>
        ///     Unregister the table binding for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <returns>
        ///     <c>true</c> if an existing binding has been removed, otherwise <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="type" /> is null.
        /// </exception>
        public bool UnregisterTableBinding(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (this.syncRoot)
            {
                this.Refresh();

                BindingSpec<ITableBinding> tableBinding;
                if (this.tableBindings.TryGetValue(type, out tableBinding))
                {
                    this.RemovingTableBinding(type, tableBinding.Binding);
                }

                this.tableBindings.Remove(from kv in this.tableBindings
                                          where kv.Value.Derived && type.IsAssignableFrom(kv.Key)
                                          select kv.Key);
                return this.tableBindings.Remove(type);
            }
        }

        /// <summary>
        ///     Creates an entity reference for the inspector specified.
        /// </summary>
        /// <returns>
        ///     The newly created entity reference.
        /// </returns>
        internal EntityReference CreateEntityReference(Inspector inspector)
        {
            var type = inspector.InspectedType;

            var entityReference = this.preliminaryEntityReferences.GetOrAdd(
                type,
                this,
                inspector,
                (t, _this, i) =>
                {
                    var columnBinding = _this.GetColumnBindingForType(t);
                    var keyBinding = _this.GetKeyBindingForType(t, columnBinding);

                    ////TODO set (or not set) Ignore flag to other valid candidates
                    if (!_this.StrictExplicitKeyBinding && i.HasProperties)
                    {
                        // Has entity key?
                        var property = i.Properties.FirstOrDefault(p => p.HasKey);
                        if (property != null)
                        {
                            if (keyBinding == null)
                            {
                                keyBinding = GetKeyBindingForProperty(property, columnBinding);
                            }

                            property.Ignore = true;
                        }

                        // Has id attribute?
                        property = i.Properties.FirstOrDefault(p => p.IdAttribute != null);
                        if (property != null)
                        {
                            if (keyBinding == null)
                            {
                                keyBinding = GetKeyBindingForProperty(property, columnBinding);
                            }

                            property.Ignore = true;
                        }

                        // Has id property?
                        if (_this.IdPropertyName != null)
                        {
                            property = i.GetProperty(_this.IdPropertyName);
                            if (property != null)
                            {
                                if (keyBinding == null)
                                {
                                    keyBinding = GetKeyBindingForProperty(property, columnBinding);
                                }

                                property.Ignore = true;
                            }
                        }
                    }

                    if (keyBinding == null)
                    {
                        return null;
                    }

                    var tableBinding = _this.GetTableBindingForType(t);
                    if (tableBinding == null)
                    {
                        var distinctTableBindings = _this.DistinctTableBindingsForType(t).ToList();
                        if (distinctTableBindings.Count() != 1)
                        {
                            return null; // none or ambiguous table bindings
                        }

                        tableBinding = distinctTableBindings.First();
                    }

                    // Has timestamp?
                    if (_this.TimestampPropertyName != null)
                    {
                        var partialKeyBinding = keyBinding as PartialKeyBinding;
                        if (partialKeyBinding != null)
                        {
                            var property = i.GetProperty(_this.TimestampPropertyName);
                            if (property != null && property.PropertyType == typeof(DateTime))
                            {
                                property.Ignore = true;
                                partialKeyBinding.TimestampAction = property.Setter;
                            }
                        }
                    }

                    return new EntityReference(t, tableBinding, columnBinding, keyBinding);
                });

            return entityReference != null ? new EntityReference(entityReference) : null;
        }

        /// <summary>
        ///     Creates an entity reference for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <returns>
        ///     The newly created entity reference.
        /// </returns>
        internal EntityReference CreateEntityReference(Type type)
        {
            if (type == null || type == typeof(object))
            {
                return null;
            }

            var inspector = Inspector.InspectorForType(type);
            if (inspector == null)
            {
                return null;
            }

            var entityReference = this.CreateEntityReference(inspector);
            if (entityReference == null && type.IsInterface)
            {
                type = TypeFinder.GetCommonBaseType(this.CommonBaseTypeCandidatesForType(type)
                    .Where(t => t != null && t != typeof(object)).Distinct());
                return this.CreateEntityReference(type);
            }

            return entityReference;
        }

        /// <param name="type">
        ///     The entity type.
        /// </param>
        protected virtual IEnumerable<Type> CommonBaseTypeCandidatesForType(Type type)
        {
            return new[]
            {
                TypeFinder.GetCommonBaseType(this.keyBindings.Keys.Where(type.IsAssignableFrom)),
                TypeFinder.GetCommonBaseType(this.tableBindings.Keys.Where(type.IsAssignableFrom)),
                TypeFinder.GetCommonBaseType(this.columnBindings.Keys.Where(type.IsAssignableFrom))
            };
        }

        /// <param name="type">
        ///     The entity type.
        /// </param>
        protected virtual IEnumerable<IColumnBinding> DistinctColumnBindingsForType(Type type)
        {
            return this.RegisteredColumnBindings().Where(kv => type.IsAssignableFrom(kv.Key)).Select(kv => kv.Value)
                .Distinct(new ColumnBindingComparer());
        }

        /// <param name="type">
        ///     The entity type.
        /// </param>
        protected virtual IEnumerable<ITableBinding> DistinctTableBindingsForType(Type type)
        {
            var types = this.RegisteredColumnBindings().Where(kv => type.IsAssignableFrom(kv.Key)).Select(kv => kv.Key)
                .ToArray();
            var bindings = this.RegisteredTableBindings();
            var distinctTableBindings = new HashSet<ITableBinding>(new TableBindingComparer());
            foreach (var t in types)
            {
                ITableBinding tb;
                if (bindings.TryGetValue(t, out tb))
                {
                    distinctTableBindings.Add(tb);
                }
            }

            return distinctTableBindings;
        }

        /// <returns>
        ///     All the registered column bindings.
        /// </returns>
        protected IDictionary<Type, IColumnBinding> RegisteredColumnBindings()
        {
            return this.registeredColumnBindings.Value.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        /// <returns>
        ///     All the registered column names, structured by column family and column qualifier.
        /// </returns>
        protected IDictionary<string, ISet<string>> RegisteredColumnNames()
        {
            // copied all the way down, and the inner copy is the one that matters: both
            // callers remove qualifiers from the sets they get back. Copying only the outer
            // map handed them the cached sets themselves, so the first entity type to
            // establish its columns emptied qualifiers out of a cache shared by every manager
            // of the factory, and a later type's scan columns depended on which type had
            // come first
            return this.registeredColumnNames.Value.ToDictionary(
                kv => kv.Key,
                kv => (ISet<string>)new HashSet<string>(kv.Value));
        }

        /// <returns>
        ///     All the registered table bindings.
        /// </returns>
        protected IDictionary<Type, ITableBinding> RegisteredTableBindings()
        {
            return this.registeredTableBindings.Value.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        /// <param name="type">
        ///     The entity type.
        /// </param>
        protected virtual void RemovingColumnBinding(Type type, IColumnBinding columnBinding)
        {
        }

        /// <param name="type">
        ///     The entity type.
        /// </param>
        protected virtual void RemovingKeyBinding(Type type, IKeyBinding keyBinding)
        {
        }

        /// <param name="type">
        ///     The entity type.
        /// </param>
        protected virtual void RemovingTableBinding(Type type, ITableBinding tableBinding)
        {
        }

        /// <summary>
        ///     Gets the key binding for the inspected property specified.
        /// </summary>
        private static IKeyBinding GetKeyBindingForProperty(InspectedProperty property, IColumnBinding columnBinding)
        {
            if (property.PropertyType == typeof(Key))
            {
                return new KeyPropertyKeyBinding(property, columnBinding);
            }

            if (property.PropertyType == typeof(string))
            {
                return new StringPropertyKeyBinding(property, columnBinding);
            }

            if (property.PropertyType == typeof(Guid))
            {
                return new GuidPropertyKeyBinding(property, columnBinding);
            }

            throw new PersistenceException(String.Format(CultureInfo.InvariantCulture,
                @"Unsupported id property type {0}", property.PropertyType));
        }

        /// <summary>
        ///     Gets the column binding for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        private IColumnBinding GetColumnBindingForType(Type type)
        {
            DefaultColumnBinding columnBinding = null;
            BindingSpec<IColumnBinding> columnBindingSpec;
            if (!this.columnBindings.TryGetValue(type, out columnBindingSpec))
            {
                var strict = this.StrictExplicitColumnBinding || type.IsInterface;
                var entityType = type;
                while (columnBindingSpec.Binding == null)
                {
                    if (!strict)
                    {
                        if (columnBinding == null)
                        {
                            columnBinding = DefaultColumnBinding.Create(type, this.DefaultColumnFamily);
                        }
                        else
                        {
                            columnBinding.Merge(type);
                        }
                    }

                    if ((columnBinding != null && columnBinding.IsComplete) || type.BaseType == null)
                    {
                        return columnBinding;
                    }

                    type = type.BaseType;
                    columnBindingSpec = this.columnBindings.GetValue(type);
                }

                this.columnBindings.AddOrUpdate(entityType, columnBindingSpec.Derive());
            }

            return columnBindingSpec.Binding;
        }

        /// <summary>
        ///     Gets the key binding for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <returns>
        ///     The kay binding.
        /// </returns>
        private IKeyBinding GetKeyBindingForType(Type type, IColumnBinding columnBinding)
        {
            var keyBinding = this.keyBindings.GetValue(type);
            var partialKeyBinding = keyBinding as PartialKeyBinding;
            if (partialKeyBinding != null && partialKeyBinding.ColumnBinding == null)
            {
                partialKeyBinding.ColumnBinding = columnBinding;
            }

            return keyBinding;
        }

        /// <summary>
        ///     Gets the table binding for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The entity type.
        /// </param>
        /// <returns>
        ///     The column binding.
        /// </returns>
        private ITableBinding GetTableBindingForType(Type type)
        {
            DefaultTableBinding defaultTableBinding = null;
            BindingSpec<ITableBinding> tableBinding;
            if (!this.tableBindings.TryGetValue(type, out tableBinding))
            {
                var strict = this.StrictExplicitTableBinding || type.IsInterface;
                var entityType = type;
                while (tableBinding.Binding == null)
                {
                    if (!strict)
                    {
                        if (defaultTableBinding == null)
                        {
                            defaultTableBinding = new DefaultTableBinding(type);
                        }
                        else
                        {
                            defaultTableBinding.Merge(type);
                        }
                    }

                    if ((defaultTableBinding != null && defaultTableBinding.IsComplete) || type.BaseType == null)
                    {
                        return defaultTableBinding;
                    }

                    type = type.BaseType;
                    tableBinding = this.tableBindings.GetValue(type);
                }

                this.tableBindings.AddOrUpdate(entityType, tableBinding.Derive());
            }

            return tableBinding.Binding;
        }

        /// <summary>
        ///     Refresh all lazy initializations.
        /// </summary>
        private void Refresh()
        {
            lock (this.syncRoot)
            {
                if (this.registeredColumnBindings == null || this.registeredColumnBindings.IsValueCreated)
                {
                    this.registeredColumnBindings = new Lazy<ConcurrentTypeDictionary<IColumnBinding>>(
                        () =>
                        {
                            var bindings = new ConcurrentTypeDictionary<IColumnBinding>();
                            foreach (var kv in this.columnBindings)
                            {
                                bindings.TryAdd(kv.Key, kv.Value.Binding);
                            }

                            return bindings;
                        });
                }

                if (this.registeredColumnNames == null || this.registeredColumnNames.IsValueCreated)
                {
                    this.registeredColumnNames = new Lazy<IDictionary<string, ISet<string>>>(
                        () =>
                        {
                            var columnNames =
                                new System.Collections.Concurrent.ConcurrentDictionary<string, ISet<string>>();
                            foreach (var binding in this.RegisteredColumnBindings().Values)
                            {
                                var columnQualifiers = columnNames.GetOrAdd(binding.ColumnFamily,
                                    _ => new HashSet<string>());
                                if (binding.ColumnQualifier != null)
                                {
                                    columnQualifiers.Add(binding.ColumnQualifier);
                                }
                            }

                            return columnNames;
                        });
                }

                if (this.registeredTableBindings == null || this.registeredTableBindings.IsValueCreated)
                {
                    this.registeredTableBindings = new Lazy<ConcurrentTypeDictionary<ITableBinding>>(
                        () =>
                        {
                            var bindings = new ConcurrentTypeDictionary<ITableBinding>();
                            foreach (var kv in this.tableBindings)
                            {
                                bindings.TryAdd(kv.Key, kv.Value.Binding);
                            }

                            return bindings;
                        });
                }
            }

            this.preliminaryEntityReferences.Clear();
        }

        /// <typeparam name="T">
        ///     The binding type.
        /// </typeparam>
        private struct BindingSpec<T>
            where T : class
        {
            public readonly T Binding;

            /// <summary>
            ///     Indicating whether this binding has been derived.
            /// </summary>
            public readonly bool Derived;

            /// <param name="derived">
            ///     Indicating whether the binding has been derived.
            /// </param>
            public BindingSpec(T binding, bool derived = false)
            {
                this.Binding = binding;
                this.Derived = derived;
            }

            /// <summary>
            ///     Creates a new derived binding.
            /// </summary>
            /// <returns>
            ///     The new derived binding.
            /// </returns>
            public BindingSpec<T> Derive()
            {
                return new BindingSpec<T>(this.Binding, true);
            }
        }
    }
}
