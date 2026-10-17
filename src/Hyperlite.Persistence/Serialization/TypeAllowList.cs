namespace Hyperlite.Persistence.Serialization
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.Reflection;
    using System.Text;
    using Hyperlite.Persistence.Reflection;

    /// <summary>
    ///     The types reading may create while <see cref="EncoderConfiguration.StrictTypes" />
    ///     is on.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A type is allowed when the application declared it, when it was allowed
    ///         explicitly, or when it is one of the framework's own value and collection
    ///         types this serializer supports. Declared means named as the type to read -
    ///         <c>FromByteArray&lt;T&gt;</c>, an entity type - or reachable from such a type
    ///         through the declared types of its members, its element types and its generic
    ///         arguments. A value whose stored type is more specific than its declaration - a
    ///         member typed <c>object</c>, an interface or a base class - is only allowed
    ///         once that type is allowed explicitly.
    ///     </para>
    ///     <para>
    ///         An array is allowed when its element type is, a generic type when its
    ///         definition and every argument are, so a type cannot be smuggled in as the
    ///         argument of one that is allowed.
    ///     </para>
    ///     <para>
    ///         The check runs when a stored type name has been resolved and before anything
    ///         is created from it, so a refused type has none of its code run. Resolving a
    ///         name can load an assembly the process can reach; it does not construct
    ///         anything or run a static constructor.
    ///     </para>
    /// </remarks>
    internal sealed class TypeAllowList
    {
        /// <summary>
        ///     The framework types with no type arguments that are allowed.
        /// </summary>
        private static readonly HashSet<Type> FrameworkTypes = new HashSet<Type>
            {
                typeof(object), typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset),
                typeof(TimeSpan), typeof(Guid), typeof(Uri), typeof(Version), typeof(StringBuilder),
                typeof(ArrayList), typeof(Hashtable), typeof(Queue), typeof(Stack), typeof(SortedList),
                typeof(BitArray),
                typeof(IEnumerable), typeof(ICollection), typeof(IList), typeof(IDictionary)
            };

        /// <summary>
        ///     The framework generic type definitions that are allowed, provided their type
        ///     arguments are.
        /// </summary>
        private static readonly HashSet<Type> FrameworkDefinitions = new HashSet<Type>
            {
                typeof(Nullable<>), typeof(KeyValuePair<,>),
                typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>), typeof(SortedSet<>),
                typeof(SortedDictionary<,>), typeof(SortedList<,>), typeof(LinkedList<>), typeof(Queue<>),
                typeof(Stack<>), typeof(Collection<>), typeof(ObservableCollection<>),
                typeof(ReadOnlyCollection<>), typeof(ConcurrentDictionary<,>), typeof(ConcurrentQueue<>),
                typeof(ConcurrentStack<>), typeof(ConcurrentBag<>),
                typeof(IEnumerable<>), typeof(ICollection<>), typeof(IList<>), typeof(ISet<>),
                typeof(IDictionary<,>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyList<>),
                typeof(IReadOnlyDictionary<,>),
                typeof(Tuple<>), typeof(Tuple<,>), typeof(Tuple<,,>), typeof(Tuple<,,,>), typeof(Tuple<,,,,>),
                typeof(Tuple<,,,,,>), typeof(Tuple<,,,,,,>), typeof(Tuple<,,,,,,,>),
                typeof(ValueTuple<>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>),
                typeof(ValueTuple<,,,,>), typeof(ValueTuple<,,,,,>), typeof(ValueTuple<,,,,,,>),
                typeof(ValueTuple<,,,,,,,>)
            };

        /// <summary>
        ///     The types allowed by declaration or explicitly.
        /// </summary>
        private readonly ConcurrentDictionary<Type, byte> declared = new ConcurrentDictionary<Type, byte>();

        /// <summary>
        ///     The assemblies whose every type is allowed.
        /// </summary>
        private readonly ConcurrentDictionary<Assembly, byte> assemblies = new ConcurrentDictionary<Assembly, byte>();

        /// <summary>
        ///     The types declared as roots, walked or waiting to be.
        /// </summary>
        private readonly ConcurrentDictionary<Type, byte> roots = new ConcurrentDictionary<Type, byte>();

        /// <summary>
        ///     The roots not walked yet. Declaring is cheap and happens whether or not strict
        ///     types are on, so that switching them on later knows every entity type already
        ///     in use; the walk is left to the first check.
        /// </summary>
        private readonly ConcurrentQueue<Type> pending = new ConcurrentQueue<Type>();

        /// <summary>
        ///     The decision made for each type checked.
        /// </summary>
        private readonly ConcurrentDictionary<Type, bool> decisions = new ConcurrentDictionary<Type, bool>();

        /// <summary>
        ///     Allows a type, and what its members declare.
        /// </summary>
        internal void Allow(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            this.Declare(type);
        }

        /// <summary>
        ///     Allows every type of an assembly.
        /// </summary>
        internal void Allow(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            if (this.assemblies.TryAdd(assembly, 0))
            {
                this.decisions.Clear();
            }
        }

        /// <summary>
        ///     Declares a type the application reads, which allows it and what its members
        ///     declare.
        /// </summary>
        internal void Declare(Type type)
        {
            if (type != null && !this.roots.ContainsKey(type) && this.roots.TryAdd(type, 0))
            {
                this.pending.Enqueue(type);
            }
        }

        /// <summary>
        ///     Returns the type when reading may create it, otherwise throws.
        /// </summary>
        /// <exception cref="SerializationException">
        ///     If the type was neither declared nor allowed.
        /// </exception>
        internal Type Check(Type type)
        {
            if (type == null || this.IsAllowed(type))
            {
                return type;
            }

            throw new SerializationException(string.Format(
                CultureInfo.InvariantCulture,
                @"{0} is not allowed: strict types are on, and it was neither declared nor allowed. If it is expected here, allow it with Encoder.Configuration.AllowType(typeof({1})).",
                type,
                type.Name));
        }

        /// <summary>
        ///     Whether reading may create the type.
        /// </summary>
        internal bool IsAllowed(Type type)
        {
            this.WalkPending();

            bool decision;
            if (this.decisions.TryGetValue(type, out decision))
            {
                return decision;
            }

            decision = this.Decide(type);
            this.decisions[type] = decision;
            return decision;
        }

        private bool Decide(Type type)
        {
            if (type.IsArray)
            {
                return this.IsAllowed(type.GetElementType());
            }

            if (type.IsPointer || type.IsByRef || type.IsGenericParameter)
            {
                return false;
            }

            if (this.declared.ContainsKey(type))
            {
                return true;
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                var definition = type.GetGenericTypeDefinition();
                if (!FrameworkDefinitions.Contains(definition) && !this.declared.ContainsKey(definition)
                    && !this.assemblies.ContainsKey(definition.Assembly))
                {
                    return false;
                }

                foreach (var argument in type.GetGenericArguments())
                {
                    if (!this.IsAllowed(argument))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (this.assemblies.ContainsKey(type.Assembly))
            {
                return true;
            }

            if (type.IsPrimitive || type.IsEnum || FrameworkTypes.Contains(type))
            {
                return true;
            }

#if !HYPERLITE_SERIALIZATION
            // an entity type is the application's by construction: only it can carry the
            // attribute, and its members are walked like any declared type's
            if (type.IsDefined(typeof(Attributes.EntityAttribute), true))
            {
                this.Declare(type);
                return true;
            }
#endif

            return false;
        }

        /// <summary>
        ///     Walks the roots declared since the last check.
        /// </summary>
        private void WalkPending()
        {
            if (this.pending.IsEmpty)
            {
                return;
            }

            Type root;
            var walked = false;
            while (this.pending.TryDequeue(out root))
            {
                this.Walk(root, new HashSet<Type>());
                walked = true;
            }

            if (walked)
            {
                // a type refused before may be declared now
                this.decisions.Clear();
            }
        }

        /// <summary>
        ///     Adds a type and everything its declaration reaches.
        /// </summary>
        private void Walk(Type type, HashSet<Type> seen)
        {
            if (type == null || !seen.Add(type))
            {
                return;
            }

            this.declared.TryAdd(type, 0);

            if (type.IsArray)
            {
                this.Walk(type.GetElementType(), seen);
                return;
            }

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    this.Walk(argument, seen);
                }
            }

            // a framework type's own members are not the application's declaration
            if (type.IsPrimitive || type.IsEnum || type.IsInterface || type.IsGenericTypeDefinition
                || FrameworkTypes.Contains(type)
                || (type.IsGenericType && FrameworkDefinitions.Contains(type.GetGenericTypeDefinition()))
                || (type.Namespace != null && (type.Namespace == "System" || type.Namespace.StartsWith("System.", StringComparison.Ordinal))))
            {
                return;
            }

            Inspector inspector;
            try
            {
                inspector = Inspector.InspectorForType(type);
            }
            catch (ArgumentException)
            {
                // a type the inspector refuses is refused again, with its reason, when it
                // is read; it declares nothing here
                return;
            }

            if (inspector != null && inspector.HasProperties)
            {
                foreach (var property in inspector.Properties)
                {
                    this.Walk(property.PropertyType, seen);
                }
            }
        }
    }
}
