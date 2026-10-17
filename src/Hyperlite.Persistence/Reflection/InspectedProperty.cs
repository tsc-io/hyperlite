namespace Hyperlite.Persistence.Reflection
{
    using System;
    using System.Reflection;
    using System.Runtime.Serialization;
    using Hyperlite.Persistence.Attributes;
    using Hyperlite.Persistence.Extensions;

    //// TODO define and implement an embed attribute (embed entities instead of writing entity references)

    internal sealed class InspectedProperty
    {
        /// <param name="type">
        ///     The inspected type.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="type" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="propertyInfo" /> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     If <paramref name="propertyInfo" /> 's declaring type has not been set.
        /// </exception>
        internal InspectedProperty(Type type, PropertyInfo propertyInfo)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (propertyInfo == null)
            {
                throw new ArgumentNullException(nameof(propertyInfo));
            }

            if (propertyInfo.DeclaringType == null)
            {
                throw new ArgumentException("fieldInfo declaring type has not been set", nameof(propertyInfo));
            }

            this.Name = propertyInfo.SerializableName();
            this.InspectedType = type;
            this.PropertyType = propertyInfo.PropertyType;
            this.IsNotNullableValueType = this.PropertyType.IsNotNullableValueType();

            if (propertyInfo.DeclaringType != propertyInfo.ReflectedType)
            {
                propertyInfo = propertyInfo.DeclaringType.GetProperty(propertyInfo.Name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            try
            {
                this.Getter = DelegateFactory.CreateGetter(propertyInfo);
                this.Setter = DelegateFactory.CreateSetter(propertyInfo);
            }
            catch (Exception exception)
            {
                Logging.TraceException(exception);
            }

            this.Member = propertyInfo;

#if!HYPERLITE_SERIALIZATION

            this.IdAttribute = propertyInfo.GetAttribute<IdAttribute>();

#endif

            this.IsTransient = propertyInfo.HasAttribute<TransientAttribute>() ||
                               propertyInfo.HasAttribute<IgnoreDataMemberAttribute>();
            this.Ignore = propertyInfo.HasAttribute<IgnoreAttribute>();
        }

        /// <param name="type">
        ///     The inspected type.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="type" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="fieldInfo" /> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     If <paramref name="fieldInfo" /> 's declaring type has not been set.
        /// </exception>
        internal InspectedProperty(Type type, FieldInfo fieldInfo)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (fieldInfo == null)
            {
                throw new ArgumentNullException(nameof(fieldInfo));
            }

            if (fieldInfo.DeclaringType == null)
            {
                throw new ArgumentException("fieldInfo declaring type has not been set", nameof(fieldInfo));
            }

            this.Name = fieldInfo.SerializableName();
            this.InspectedType = type;
            this.PropertyType = fieldInfo.FieldType;
            this.IsNotNullableValueType = this.PropertyType.IsNotNullableValueType();

            if (fieldInfo.DeclaringType != fieldInfo.ReflectedType)
            {
                fieldInfo = fieldInfo.DeclaringType.GetField(fieldInfo.Name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            try
            {
                this.Getter = DelegateFactory.CreateGetter(fieldInfo);
                this.Setter = DelegateFactory.CreateSetter(fieldInfo);
            }
            catch (Exception exception)
            {
                Logging.TraceException(exception);
            }

            this.Member = fieldInfo;

#if!HYPERLITE_SERIALIZATION

            this.IdAttribute = fieldInfo.GetAttribute<IdAttribute>();

#endif
            this.IsTransient = fieldInfo.HasAttribute<TransientAttribute>() ||
                               fieldInfo.HasAttribute<NonSerializedAttribute>()
                               || fieldInfo.HasAttribute<IgnoreDataMemberAttribute>();

            this.Ignore = fieldInfo.HasAttribute<IgnoreAttribute>();
        }

        /// <value>
        ///     The getter function.
        /// </value>
        internal Func<object, object> Getter { get; }

        /// <summary>
        ///     Gets a value indicating whether the inspected property has a getter.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected property has a getter, otherwise <c>false</c>.
        /// </value>
        internal bool HasGetter => this.Getter != null;

#if!HYPERLITE_SERIALIZATION

        /// <summary>
        ///     Gets a value indicating whether the inspected property is of type <see cref="Hyperlite.Key" />.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected property is of type <see cref="Hyperlite.Key" />, otherwise <c>false</c>.
        /// </value>
        internal bool HasKey => this.PropertyType == typeof(Key);

#endif

        /// <summary>
        ///     Gets a value indicating whether the inspected property has a setter.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected property has a setter, otherwise <c>false</c>.
        /// </value>
        internal bool HasSetter => this.Setter != null;

#if!HYPERLITE_SERIALIZATION

        /// <value>
        ///     The identifier attribute or null.
        /// </value>
        internal IdAttribute IdAttribute { get; }

#endif

        /// <summary>
        ///     Gets or sets a value indicating whether to ignore this property on serialization.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the serializer should ignore this property, otherwise <c>false</c>.
        /// </value>
        internal bool Ignore { get; set; }

        internal Type InspectedType { get; }

        /// <summary>
        ///     Gets a value indicating whether the inspected property type is a not nullable value type.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the inspected property type is a not nullable value type, otherwise <c>false</c>.
        /// </value>
        internal bool IsNotNullableValueType { get; }

        /// <summary>
        ///     Gets a value indicating whether IsTransient.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the property is transient, otherwise <c>false</c>.
        /// </value>
        internal bool IsTransient { get; }

        internal MemberInfo Member { get; }

        /// <summary>
        ///     Gets the property name.
        /// </summary>
        /// <value>
        ///     The property name.
        /// </value>
        internal string Name { get; }

        internal Type PropertyType { get; }

        internal Action<object, object> Setter { get; }
    }
}
