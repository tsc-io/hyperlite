namespace Hyperlite.Persistence.Reflection
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Runtime.Serialization;
    using System.Text.RegularExpressions;
    using Hyperlite.Persistence.Collections;
    using Hyperlite.Persistence.Collections.Concurrent;
    using Hyperlite.Persistence.Extensions;

    internal sealed class Inspector
    {
        private static readonly ConcurrentTypeDictionary<Inspector> Inspectors =
            new ConcurrentTypeDictionary<Inspector>();

        /// <summary>
        ///     Indicating whether the inspected type has a default constructor.
        /// </summary>
        private readonly Func<object> createInstance;

        private readonly InspectedEnumerable enumerable;

        /// <summary>
        ///     Indicating whether the inspected type has a serialization handlers.
        /// </summary>
        private readonly bool hasSerializationHandlers;

        private readonly IDictionary<string, InspectedProperty> inspectedProperties;

        private readonly KeyValuePair<string, InspectedProperty>[] inspectedPropertiesPositional;

        /// <summary>
        ///     The inspected property per regular expression.
        /// </summary>
        private readonly ConcurrentStringDictionary<InspectedProperty> regexProperties =
            new ConcurrentStringDictionary<InspectedProperty>();

        private readonly InspectedSerializable serializable;

        /// <param name="type">
        ///     The type to inspect.
        /// </param>
        private Inspector(Type type)
        {
            this.InspectedType = type;

            if (this.IsArray = type.IsArray)
            {
                return;
            }

            // the serializer used to ask "value is IDictionary" and "value is IEnumerable" per
            // written object, both only depend on the inspected type
            this.ImplementsDictionary = typeof(System.Collections.IDictionary).IsAssignableFrom(type);
            this.ImplementsEnumerable = typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

            if (this.IsEnum = type.IsEnum)
            {
                this.EnumType = type.GetEnumUnderlyingType();
                return;
            }

            if (this.IsTuple = type.IsTuple())
            {
                return;
            }

            if (this.IsKeyValuePair = type.IsGenericTypeDefinition(typeof(KeyValuePair<,>)))
            {
                return;
            }

            if (!typeof(Enumerable).IsAssignableFrom(type))
            {
                this.createInstance = DelegateFactory.CreateInstance(type);

                ////TODO control isSerializable/ISerializable by settings fields or props?
                this.inspectedProperties = ReflectionExtensions.HasAttribute<SerializableAttribute>(type)
                    ? InspectFields(type)
                    : InspectProperties(type);

                if (typeof(IDictionary).IsAssignableFrom(type))
                {
                    ////TODO impl support for comparer (also for set's)
                }
                else if (typeof(IEnumerable).IsAssignableFrom(type))
                {
                    this.enumerable = InspectEnumerable(type);
                    if (this.enumerable != null && type.Namespace.StartsWith("System.Collections"))
                    {
                        this.inspectedProperties.Clear();
                    }
                }
                else if (type.HasInterface(typeof(ISerializable)))
                {
                    this.serializable = InspectSerializable(type);
                    if (this.serializable != null)
                    {
                        this.inspectedProperties.Clear();
                    }
                }

                this.inspectedPropertiesPositional = this.inspectedProperties.ToArray();

                this.OnSerializing = CreateHandler<OnSerializingAttribute>(type);
                this.OnSerialized = CreateHandler<OnSerializedAttribute>(type);
                this.OnDeserializing = CreateHandler<OnDeserializingAttribute>(type);
                this.OnDeserialized = CreateHandler<OnDeserializedAttribute>(type);
                this.hasSerializationHandlers = this.OnSerializing != null || this.OnSerialized != null ||
                                                this.OnDeserializing != null || this.OnDeserialized != null;
            }
            else
            {
                this.enumerable = InspectEnumerable(type);
            }
        }

        internal InspectedEnumerable Enumerable => this.enumerable;

        /// <summary>
        ///     Gets the enum type if the inspected type is an enumeration.
        /// </summary>
        internal Type EnumType { get; }

        /// <summary>
        ///     Gets a value indicating whether the inspector has properties.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspector has properties, otherwise <c>false</c>.
        /// </value>
        internal bool HasProperties => this.inspectedProperties?.Count > 0;

        internal Type InspectedType { get; }

        /// <summary>
        ///     Gets a value indicating whether the inspected type is an array.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected type is an array, otherwise <c>false</c>.
        /// </value>
        internal bool IsArray { get; }

        /// <summary>
        ///     Gets a value indicating whether the inspector is a collection.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspector is a collection, otherwise <c>false</c>.
        /// </value>
        internal bool IsCollection => this.enumerable != null && this.enumerable.HasAdd;

        /// <summary>
        ///     Gets a value indicating whether the inspected type is an enum.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected type is an enum, otherwise <c>false</c>.
        /// </value>
        internal bool IsEnum { get; }

        /// <summary>
        ///     Gets a value indicating whether the inspected type implements IDictionary.
        /// </summary>
        internal bool ImplementsDictionary { get; }

        /// <summary>
        ///     Gets a value indicating whether the inspected type implements IEnumerable.
        /// </summary>
        internal bool ImplementsEnumerable { get; }

        /// <summary>
        ///     Gets a value indicating whether the inspector is an enumerable.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspector is an enumerable, otherwise <c>false</c>.
        /// </value>
        internal bool IsEnumerable => this.enumerable != null;

        /// <summary>
        ///     Gets a value indicating whether the inspected type is a key value pair.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected type is key value pair, otherwise <c>false</c>.
        /// </value>
        internal bool IsKeyValuePair { get; }

        /// <summary>
        ///     Gets a value indicating whether the inspector is a serializable.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspector is a serializable, otherwise <c>false</c>.
        /// </value>
        internal bool IsSerializable => this.serializable != null;

        /// <summary>
        ///     Gets a value indicating whether the inspected type is a tuple.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected type is a tuple, otherwise <c>false</c>.
        /// </value>
        internal bool IsTuple { get; }

        /// <value>
        ///     The OnDeserialized handler or null.
        /// </value>
        internal Action<object, StreamingContext> OnDeserialized { get; }

        /// <value>
        ///     The OnDeserializing handler or null.
        /// </value>
        internal Action<object, StreamingContext> OnDeserializing { get; }

        /// <value>
        ///     The OnSerialized handler or null.
        /// </value>
        internal Action<object, StreamingContext> OnSerialized { get; }

        /// <value>
        ///     The OnSerializing handler or null.
        /// </value>
        internal Action<object, StreamingContext> OnSerializing { get; }

        /// <summary>
        ///     Gets the inspected properties.
        /// </summary>
        /// <value>
        ///     The inspected properties.
        /// </value>
        internal ICollection<InspectedProperty> Properties => this.inspectedProperties.Values;

        /// <value>
        ///     The inspected enumerable.
        /// </value>
        internal InspectedSerializable Serializable => this.serializable;

        /// <param name="type">
        ///     The type to inspect.
        /// </param>
        internal static Inspector InspectorForType(Type type)
        {
            return Inspectors.GetOrAdd(type, t => new Inspector(t));
        }

        /// <summary>
        ///     Creates an instance of the inspected type.
        /// </summary>
        /// <returns>
        ///     The newly created instance.
        /// </returns>
        internal object CreateInstance()
        {
            if (!this.InspectedType.IsInterface && !this.InspectedType.IsAbstract)
            {
                return this.createInstance();
            }

            return null;
        }

        /// <summary>
        ///     Gets the inspected property by the name specified.
        /// </summary>
        /// <param name="name">
        ///     The property name.
        /// </param>
        /// <returns>
        ///     The inspected property or null.
        /// </returns>
        internal InspectedProperty GetProperty(string name, int positionalHint)
        {
            if (positionalHint >= 0 && positionalHint < this.inspectedPropertiesPositional.Length)
            {
                var entry = this.inspectedPropertiesPositional[positionalHint];
                // the propertyNameComparer is not used for performance reason
                if (string.Equals(name, entry.Key))
                {
                    return entry.Value;
                }
            }

            InspectedProperty inspectedProperty;
            this.inspectedProperties.TryGetValue(name, out inspectedProperty);
            return inspectedProperty;
        }

        /// <summary>
        ///     Gets the inspected property by the alternate name.
        /// </summary>
        /// <param name="name">
        ///     The property name.
        /// </param>
        /// <param name="alternateName">
        ///     The alternate property name.
        /// </param>
        /// <returns>
        ///     The inspected property or null.
        /// </returns>
        /// <remarks>
        ///     This used to record the alternate name as a second key for the member, so the
        ///     resolver was asked once per name for the life of the process. The names come
        ///     from the bytes, so the record grew with every distinct one; it was written under
        ///     a lock while other threads read it without one; and its values are the members a
        ///     type schema is made of, so a type first written after an alias wrote that member
        ///     twice. Nothing is recorded now: the deserializer keeps the answer on the stream's
        ///     own type schema, which asks the resolver once per stream.
        /// </remarks>
        internal InspectedProperty GetProperty(string name, string alternateName)
        {
            InspectedProperty inspectedProperty = null;
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(alternateName))
            {
                this.inspectedProperties.TryGetValue(name, out inspectedProperty);
            }

            return inspectedProperty;
        }

        /// <summary>
        ///     Gets the inspected property by the regex specified.
        /// </summary>
        /// <param name="regex">
        ///     The regular expression to match.
        /// </param>
        /// <returns>
        ///     The inspected property or null.
        /// </returns>
        internal InspectedProperty GetProperty(Regex regex)
        {
            return regex == null
                ? null
                : this.regexProperties.GetOrAdd(
                    regex.ToString(),
                    this,
                    regex,
                    (n, _this, _regex) => _this.Properties.FirstOrDefault(p => _regex.Match(p.Name).Success));
        }

        /// <summary>
        ///     Create a handler for serialization attribute specified.
        /// </summary>
        /// <typeparam name="T">
        ///     The serialization attribute.
        /// </typeparam>
        /// <returns>
        ///     The newly created handler or null.
        /// </returns>
        private static Action<object, StreamingContext> CreateHandler<T>(Type type) where T : Attribute
        {
            var methods = type.GetMethodsWithAttribute<T>();
            if (methods != null)
            {
                var handler = new DynamicMethod("hyperlite_Handler" + methods[0].Name, typeof(void),
                    new[] { typeof(object), typeof(StreamingContext) }, methods[0].Module, true)
                { InitLocals = false };
                var generator = handler.GetILGenerator();

                foreach (var m in methods)
                {
                    generator.Emit(OpCodes.Ldarg_0);
                    generator.Emit(OpCodes.Ldarg_1);
                    generator.Emit(OpCodes.Call, m);
                }

                generator.Emit(OpCodes.Ret);

                return (Action<object, StreamingContext>)handler.CreateDelegate(
                    typeof(Action<object, StreamingContext>));
            }

            return null;
        }

        /// <summary>
        ///     Inspects enumerable for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The type to inspect.
        /// </param>
        private static InspectedEnumerable InspectEnumerable(Type type)
        {
            return new InspectedEnumerable(type);
        }

        /// <summary>
        ///     Throws if a member name matches one already taken only when case is ignored.
        /// </summary>
        /// <remarks>
        ///     Members are matched ignoring case, so of two whose names differ only in case -
        ///     <c>Value</c> and <c>value</c>, legal C# - only one could ever be serialized, and
        ///     which one would be a silent choice. That is refused rather than made. The same
        ///     name met again exactly is not a conflict: walking the base types revisits every
        ///     inherited member, and a member hidden with <c>new</c> keeps its derived one.
        /// </remarks>
        private static void ThrowIfCaseOnlyDuplicate(Type type, string kind, string name, HashSet<string> added)
        {
            if (added.Contains(name))
            {
                return;
            }

            var other = added.First(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Ambiguous {0} in {1}: '{2}' and '{3}' differ only in case. Members are matched ignoring case, so only one of them could be serialized - rename one, or mark one transient.",
                    kind,
                    type,
                    other,
                    name),
                nameof(type));
        }

        /// <summary>
        ///     Inspects the fields for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The type to inspect.
        /// </param>
        /// <returns>
        ///     The inspected fields.
        /// </returns>
        private static IDictionary<string, InspectedProperty> InspectFields(Type type)
        {
            var inspectedProperties = new StringDictionary<InspectedProperty, StringComparerOrdinalIgnoreCase>();
            var added = new HashSet<string>(System.StringComparer.Ordinal);
            for (var t = type; t != typeof(object) && t != null; t = t.BaseType)
            {
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.FlattenHierarchy);

                //// TODO correct flattening (duplicated private field names)
                //// TODO add any filter if required
                foreach (var field in fields)
                {
                    if (!field.FieldType.IsTransient())
                    {
                        var name = field.SerializableName();
                        if (!inspectedProperties.ContainsKey(name))
                        {
                            var inspectedProperty = new InspectedProperty(type, field);
                            if (inspectedProperty.HasSetter && inspectedProperty.HasGetter &&
                                !inspectedProperty.IsTransient)
                            {
                                inspectedProperties.Add(name, inspectedProperty);
                                added.Add(name);
                            }
                        }
                        else
                        {
                            ThrowIfCaseOnlyDuplicate(type, "field", name, added);
                        }
                    }
                }
            }

            return inspectedProperties;
        }

        /// <summary>
        ///     Inspects the properties for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The type to inspect.
        /// </param>
        /// <returns>
        ///     The inspected properties.
        /// </returns>
        private static IDictionary<string, InspectedProperty> InspectProperties(Type type)
        {
            var inspectedProperties = new StringDictionary<InspectedProperty, StringComparerOrdinalIgnoreCase>();
            var added = new HashSet<string>(System.StringComparer.Ordinal);
            for (var t = type; t != typeof(object) && t != null; t = t.BaseType)
            {
                var properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                                 BindingFlags.FlattenHierarchy);

                //// TODO correct flattening (duplicated private property names)
                //// TODO add any filter if required
                foreach (var property in properties)
                {
                    if (!property.PropertyType.IsTransient())
                    {
                        var name = property.SerializableName();
                        if (!inspectedProperties.ContainsKey(name))
                        {
                            // Ignore indexer
                            if (property.GetIndexParameters().Length == 0)
                            {
                                var inspectedProperty = new InspectedProperty(type, property);
                                if (inspectedProperty.HasSetter && inspectedProperty.HasGetter &&
                                    !inspectedProperty.IsTransient)
                                {
                                    inspectedProperties.Add(name, inspectedProperty);
                                    added.Add(name);
                                }
                            }
                        }
                        else
                        {
                            ThrowIfCaseOnlyDuplicate(type, "property", name, added);
                        }
                    }
                }
            }

            return inspectedProperties;
        }

        /// <summary>
        ///     Inspects serializable for the type specified.
        /// </summary>
        /// <param name="type">
        ///     The type to inspect.
        /// </param>
        private static InspectedSerializable InspectSerializable(Type type)
        {
            var inspectedSerializable = new InspectedSerializable(type);
            return inspectedSerializable.CreateInstance != null ? inspectedSerializable : null;
        }
    }
}
