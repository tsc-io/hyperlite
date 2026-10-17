namespace Hyperlite.Persistence.Reflection
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    internal sealed class InspectedEnumerable
    {
        /// <summary>
        ///     The collections whose element adder is not called Add.
        /// </summary>
        /// <remarks>
        ///     An allowlist keyed on the generic type definition, rather than a lookup of the
        ///     explicitly implemented <c>ICollection&lt;T&gt;.Add</c> through the interface
        ///     map. The interface map would find LinkedList's adder as well, and would also
        ///     find ReadOnlyCollection's, whose Add throws - which would turn a type that
        ///     serializes today into one that fails while being read. These three are the
        ///     ones that were losing their contents, so these three are the ones listed.
        /// </remarks>
        private static readonly Dictionary<Type, string> KnownAdders = new Dictionary<Type, string>
            {
                { typeof(LinkedList<>), "AddLast" },
                { typeof(Queue<>), "Enqueue" },
                { typeof(Stack<>), "Push" }
            };

        private Func<object, object, object> addFunc;

        /// <param name="type">
        ///     The type to inspect.
        /// </param>
        internal InspectedEnumerable(Type type)
        {
            this.InspectedType = type;
            this.AddsInReverse = type.IsGenericType
                                 && type.GetGenericTypeDefinition() == typeof(Stack<>);

            if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            {
                this.ElementType = type.GetGenericArguments()[0];
            }
            else
            {
                this.ElementType = type.GetInterfaces()
                    .Where(t => typeof(IEnumerable).IsAssignableFrom(t) && t.IsGenericType && t.GetGenericArguments().Length == 1)
                    .FirstOrDefault()?.GetGenericArguments()[0] ?? typeof(object);
            }

            try
            {
                this.Add = this.CreateAddMethod(type);
                this.Count = CreateCountMethod(type);
                this.Capacity = CreateCapacityMethod(type);
                this.Indexer = CreateIndexerMethod(type);
            }
            catch (Exception exception)
            {
                Logging.TraceException(exception);
            }
        }

        internal Action<object, object> Add { get; }

        /// <summary>
        ///     Gets a value indicating whether the elements have to be added in reverse.
        /// </summary>
        /// <value>
        ///     <c>true</c> for a stack, otherwise <c>false</c>.
        /// </value>
        /// <remarks>
        ///     A stack enumerates from its top, which is the last element pushed, so the form
        ///     on the wire holds the elements in the reverse of the order they were pushed in.
        ///     Pushing them back in the order they are read would return the stack reversed.
        /// </remarks>
        internal bool AddsInReverse { get; }

        /// <summary>
        ///     Returns what the elements should be added to while reading.
        /// </summary>
        /// <remarks>
        ///     The instance itself, except for a collection that has to be filled backwards -
        ///     that gets a buffer, because the instance has to exist and be registered as a
        ///     back-reference target before its elements are read, and a stack cannot be
        ///     filled from the bottom.
        /// </remarks>
        /// <param name="instance">
        ///     The collection being read.
        /// </param>
        /// <returns>
        ///     The instance, or a buffer to be handed back to <see cref="CompleteFill" />.
        /// </returns>
        internal object CreateFillTarget(object instance)
        {
            return this.AddsInReverse
                ? Activator.CreateInstance(typeof(List<>).MakeGenericType(this.ElementType))
                : instance;
        }

        /// <summary>
        ///     Adds one element to what <see cref="CreateFillTarget" /> returned.
        /// </summary>
        /// <param name="target">
        ///     The fill target.
        /// </param>
        /// <param name="value">
        ///     The element.
        /// </param>
        internal void Fill(object target, object value)
        {
            if (this.AddsInReverse)
            {
                ((IList)target).Add(value);
                return;
            }

            this.Add(target, value);
        }

        /// <summary>
        ///     Moves a buffer's elements into the instance, in reverse. Does nothing when
        ///     <see cref="CreateFillTarget" /> handed back the instance itself.
        /// </summary>
        /// <param name="instance">
        ///     The collection being read.
        /// </param>
        /// <param name="target">
        ///     What <see cref="CreateFillTarget" /> returned.
        /// </param>
        internal void CompleteFill(object instance, object target)
        {
            if (!this.AddsInReverse)
            {
                return;
            }

            var buffer = (IList)target;
            for (var n = buffer.Count - 1; n >= 0; --n)
            {
                this.Add(instance, buffer[n]);
            }
        }

        internal Action<object, object> Capacity { get; }

        /// <value>
        ///     The count function.
        /// </value>
        internal Func<object, object> Count { get; }

        internal Type ElementType { get; }

        /// <summary>
        ///     Gets a value indicating whether the inspected enumerable has an add action.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected enumerable has an add action, otherwise <c>false</c>.
        /// </value>
        internal bool HasAdd => this.Add != null;

        /// <summary>
        ///     Gets a value indicating whether the inspected enumerable has a capacity action.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected enumerable has a capacity action, otherwise <c>false</c>.
        /// </value>
        internal bool HasCapacity => this.Capacity != null;

        /// <summary>
        ///     Gets a value indicating whether the inspected enumerable has a count function.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected enumerable has a count function, otherwise <c>false</c>.
        /// </value>
        internal bool HasCount => this.Count != null;

        /// <summary>
        ///     Gets a value indicating whether the inspected enumerable has an indexer.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected enumerable has an indexer, otherwise <c>false</c>.
        /// </value>
        internal bool HasIndexer => this.Indexer != null;

        internal Action<object, int, object> Indexer { get; }

        internal Type InspectedType { get; }

        /// <summary>
        ///     Create the capacity action for the type specified.
        /// </summary>
        /// <returns>
        ///     The newly created capacity action or null.
        /// </returns>
        private static Action<object, object> CreateCapacityMethod(Type type)
        {
            return DelegateFactory.CreateSetter(GetProperty(type, "Capacity", typeof(int), Type.EmptyTypes));
        }

        /// <summary>
        ///     Create the count function for the type specified.
        /// </summary>
        /// <returns>
        ///     The newly created count function or null.
        /// </returns>
        private static Func<object, object> CreateCountMethod(Type type)
        {
            return DelegateFactory.CreateGetter(GetProperty(type, "Count", typeof(int), Type.EmptyTypes));
        }

        /// <summary>
        ///     Create the indexer action for the type specified.
        /// </summary>
        /// <returns>
        ///     The newly created indexer action or null.
        /// </returns>
        private static Action<object, int, object> CreateIndexerMethod(Type type)
        {
            return DelegateFactory.CreateIndexerSetter(GetProperty(type, "Item", null, new[] { typeof(int) }));
        }

        /// <summary>
        ///     Gets the instance method with the name and single parameter type specified.
        /// </summary>
        /// <param name="name">
        ///     The method name.
        /// </param>
        /// <returns>
        ///     The method info, or null if the type has no such method or more than one matches.
        /// </returns>
        private static MethodInfo GetMethod(Type type, string name, Type parameterType)
        {
            try
            {
                return type.GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy,
                    null,
                    new[] { parameterType },
                    null);
            }
            catch (AmbiguousMatchException)
            {
                return null;
            }
        }

        /// <summary>
        ///     Gets the instance property with the name, property type and index parameter types specified.
        /// </summary>
        /// <param name="name">
        ///     The property name.
        /// </param>
        /// <param name="returnType">
        ///     The property type, or null to accept any property type.
        /// </param>
        /// <param name="types">
        ///     The index parameter types.
        /// </param>
        /// <returns>
        ///     The property info, or null if the type has no such property or more than one matches.
        /// </returns>
        private static PropertyInfo GetProperty(Type type, string name, Type returnType, Type[] types)
        {
            try
            {
                return type.GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy,
                    null,
                    returnType,
                    types,
                    null);
            }
            catch (AmbiguousMatchException)
            {
                return null;
            }
        }

        /// <summary>
        ///     Create the add action for the type specified.
        /// </summary>
        /// <returns>
        ///     The newly created add action or null.
        /// </returns>
        private Action<object, object> CreateAddMethod(Type type)
        {
            var methodInfo = GetMethod(type, "AddInternal", this.ElementType) ??
                             GetMethod(type, "Add", this.ElementType) ??
                             KnownAdder(type, this.ElementType);

            if (methodInfo == null)
            {
                return null;
            }

            if (methodInfo.ReturnType == typeof(void))
            {
                return DelegateFactory.CreateAction(methodInfo);
            }

            if ((this.addFunc = DelegateFactory.CreateFunc(methodInfo)) == null)
            {
                return null;
            }

            return this.InvokeAddFunc;
        }

        /// <summary>
        ///     The adder of a collection that does not call it Add, or null.
        /// </summary>
        /// <returns>
        ///     The adder, or null when the type is not one of the listed ones.
        /// </returns>
        private static MethodInfo KnownAdder(Type type, Type elementType)
        {
            string name;
            if (!type.IsGenericType
                || !KnownAdders.TryGetValue(type.GetGenericTypeDefinition(), out name))
            {
                return null;
            }

            return type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { elementType },
                null);
        }

        /// <summary>
        ///     Invokes the add function on the target instance specified.
        /// </summary>
        /// <param name="param">
        ///     The value to add.
        /// </param>
        private void InvokeAddFunc(object target, object param)
        {
            this.addFunc(target, param);
        }
    }
}
