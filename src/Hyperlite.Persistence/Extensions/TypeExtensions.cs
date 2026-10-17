namespace Hyperlite.Persistence.Extensions
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Text;
    using Hyperlite.Persistence.Collections.Concurrent;

    internal static class TypeExtensions
    {
        private static readonly ConcurrentTypeDictionary<bool> ComplexTypes = new ConcurrentTypeDictionary<bool>();

        /// <summary>
        ///     The complex types.
        /// </summary>
        private static readonly ConcurrentTypeDictionary<Func<object, object>> EnumToObject =
            new ConcurrentTypeDictionary<Func<object, object>>();

        private static readonly Type TypeITuple =
            typeof(Tuple<int>).GetInterface("System.Runtime.CompilerServices.ITuple") ??
            typeof(Tuple<int>).GetInterface("System.ITuple");

#if !NET8_0_OR_GREATER
        /// <summary>
        ///     The value types. A cache only where it pays - see <see cref="IsValueType" />.
        /// </summary>
        private static readonly ConcurrentTypeDictionary<bool> ValueTypes = new ConcurrentTypeDictionary<bool>();
#endif

        /// <summary>
        ///     Converts a value to the type specified.
        /// </summary>
        /// <param name="value">
        ///     The value to convert.
        /// </param>
        /// <returns>
        ///     The converted value.
        /// </returns>
        internal static object Convert(this Type destinationType, object value)
        {
            if (value == null || destinationType == null || destinationType.IsInstanceOfType(value))
            {
                return value;
            }

            // A Nullable<T> converts exactly as T does, and the box that comes back is a
            // valid Nullable<T>, so unwrap first and let the rest of the method deal with
            // the underlying type.
            //
            // Doing it here rather than further down is what lets a nullable enum reach
            // the enum branch below. Nullable<TEnum> is not itself an enum, so IsEnum
            // missed it and the value came back as the underlying integer: a
            // FromByteArray(typeof(TEnum?), ...) returned an Int32, the generic overload
            // threw InvalidCastException on the way out, and an array of nullable enum
            // threw as its elements were stored. The bytes were always right - this is the
            // reader deciding the wrong .NET type for them.
            if (destinationType.IsNullable())
            {
                destinationType = Nullable.GetUnderlyingType(destinationType);

                if (destinationType.IsInstanceOfType(value))
                {
                    return value;
                }
            }

            var enumToObject = EnumToObject.GetOrAdd(
                destinationType,
                value,
                (type, _value) =>
                {
                    if (type.IsEnum)
                    {
                        switch (System.Convert.GetTypeCode(_value))
                        {
                            case TypeCode.Int32:
                                return (v => Enum.ToObject(type, (int)v));

                            case TypeCode.SByte:
                                return (v => Enum.ToObject(type, (sbyte)v));

                            case TypeCode.Int16:
                                return (v => Enum.ToObject(type, (short)v));

                            case TypeCode.Int64:
                                return (v => Enum.ToObject(type, (long)v));

                            case TypeCode.UInt32:
                                return (v => Enum.ToObject(type, (uint)v));

                            case TypeCode.Byte:
                                return (v => Enum.ToObject(type, (byte)v));

                            case TypeCode.UInt16:
                                return (v => Enum.ToObject(type, (ushort)v));

                            case TypeCode.UInt64:
                                return (v => Enum.ToObject(type, (ulong)v));

                            default:
                                throw new ArgumentException("Invalid type code");
                        }
                    }

                    return null;
                });

            if (enumToObject != null)
            {
                return enumToObject(value);
            }

            if (destinationType == typeof(string))
            {
                return value.ToString();
            }

            var text = value as string;
            if (text != null)
            {
                return destinationType == typeof(StringBuilder)
                    ? new StringBuilder(text)
                    : System.Convert.ChangeType(text, destinationType, CultureInfo.CurrentCulture);
            }

            if (destinationType.IsPrimitive)
            {
                return System.Convert.ChangeType(value, destinationType, CultureInfo.CurrentCulture);
            }

            if (destinationType.IsArray)
            {
                var elementType = destinationType.GetElementType();
                var array = Array.CreateInstance(elementType, 1);
                array.SetValue(Convert(elementType, value), 0);
                return array;
            }

            if (typeof(IList).IsAssignableFrom(destinationType))
            {
                var list = (IList)Activator.CreateInstance(destinationType, true);
                list.Add(Convert(
                    destinationType.IsGenericType ? destinationType.GetGenericArguments()[0] : typeof(object), value));
                return list;
            }

            return value;
        }

        /// <summary>
        ///     Gets a single attribute from the type specified.
        /// </summary>
        /// <param name="inherit">
        ///     <c>true</c> to search this member's inheritance chain to find the attributes, otherwise <C>False</C>.
        /// </param>
        /// <typeparam name="T">
        ///     The attribute type.
        /// </typeparam>
        /// <returns>
        ///     The attribute instance or null.
        /// </returns>
        internal static T GetAttribute<T>(this Type type, bool inherit = false) where T : Attribute
        {
            var attributes = type.GetCustomAttributes(typeof(T), inherit);
            return attributes.Length > 0 ? (T)attributes[0] : null;
        }

        /// <summary>
        ///     Get all methods with attribute specified.
        /// </summary>
        /// <typeparam name="T">
        ///     The attribute type.
        /// </typeparam>
        /// <returns>
        ///     The method list or null.
        /// </returns>
        internal static List<MethodInfo> GetMethodsWithAttribute<T>(this Type type) where T : Attribute
        {
            return GetMethodsWithAttribute(type, typeof(T));
        }

        /// <returns>
        ///     The method list or null.
        /// </returns>
        internal static List<MethodInfo> GetMethodsWithAttribute(this Type type, Type attribute)
        {
            var methodInfos = new List<MethodInfo>();
            var current = type;
            while (current != null && current != typeof(object))
            {
                var methods = current.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic |
                                           BindingFlags.Public);

                foreach (var methodInfo in methods)
                {
                    if (methodInfo.IsDefined(attribute, false))
                    {
                        methodInfos.Add(methodInfo);
                    }
                }

                current = current.BaseType;
            }

            methodInfos.Reverse(); // We should invoke the methods starting from base
            return methodInfos.Count > 0 ? methodInfos : null;
        }

        /// <summary>
        ///     Returns a value indicating whether the type has the attribute declared.
        /// </summary>
        /// <param name="inherit">
        ///     <c>true</c> to search this member's inheritance chain to find the attributes, otherwise <C>False</C>.
        /// </param>
        /// <typeparam name="T">
        ///     The attribute type.
        /// </typeparam>
        /// <returns>
        ///     <c>true</c> if the type has the attribute declared, otherwise <c>false</c>.
        /// </returns>
        internal static bool HasAttribute<T>(this Type type, bool inherit = false) where T : Attribute
        {
            return type.GetCustomAttributes(typeof(T), inherit).Length > 0;
        }

        /// <summary>
        ///     Returns a value indicating whether the type implements the interface specified.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the type implements the interface specified, otherwise <c>false</c>.
        /// </returns>
        internal static bool HasInterface(this Type type, Type typeInterface)
        {
            if (SameMetadataToken(type, typeInterface))
            {
                return true;
            }

            foreach (var ifc in type.GetInterfaces())
            {
                if (SameMetadataToken(ifc, typeInterface))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Gets a value indicating whether the type is complex.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the type is complex, otherwise <c>false</c>.
        /// </returns>
        internal static bool IsComplex(this Type type)
        {
            return ComplexTypes.GetOrAdd(type,
                t => t != typeof(string) && t != typeof(Type).GetType() && t.IsClass);
        }

        /// <summary>
        ///     Gets a value indicating whether the type is a delegate.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the type is a delegate, otherwise <c>false</c>.
        /// </returns>
        internal static bool IsDelegate(this Type type)
        {
            return typeof(Delegate).IsAssignableFrom(type)
                   || (type.IsGenericType
                       && (type.GetGenericTypeDefinition() == typeof(Action<>) ||
                           type.GetGenericTypeDefinition() == typeof(Func<>)
                           || type.GetGenericTypeDefinition() == typeof(Expression<>)));
        }

        /// <summary>
        ///     Gets a value indicating whether the type is the generic type definition specified.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the type is the generic type definition specified, otherwise <c>false</c>.
        /// </returns>
        internal static bool IsGenericTypeDefinition(this Type type, Type genericTypeDefinition)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition;
        }

        /// <summary>
        ///     Gets a value indicating whether the type is not nullable value type.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the type is not nullable value type, otherwise <c>false</c>.
        /// </returns>
        internal static bool IsNotNullableValueType(this Type type)
        {
            return type.IsValueType() && !IsNullable(type);
        }

        /// <summary>
        ///     Gets a value indicating whether the type is a nullable type.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the type is a nullable type, otherwise <c>false</c>.
        /// </returns>
        internal static bool IsNullable(this Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        /// <summary>
        ///     Gets a value indicating whether the type is transient.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the type is transient, otherwise <c>false</c>.
        /// </returns>
        internal static bool IsTransient(this Type type)
        {
            return IsDelegate(type) || type == typeof(IntPtr);
        }

        /// <summary>
        ///     Gets a value indicating whether the type is a tuple.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the type is a tuple, otherwise <c>false</c>.
        /// </returns>
        internal static bool IsTuple(this Type type)
        {
            return TypeITuple.IsAssignableFrom(type);
        }

        /// <summary>
        ///     Gets a value indicating whether the type is a value type.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the type is a value, otherwise <c>false</c>.
        /// </returns>
        /// <remarks>
        ///     Runs once per object written. On net48 <see cref="Type.IsValueType" /> is a
        ///     reflection call and the cache halves it - reading the property directly measured
        ///     111% slower. On .NET 8 and later the property is cheap and the cache is the cost:
        ///     reading it directly measured 75-79% faster.
        ///     The netstandard2.0 build keeps the cache, because it may run on either and
        ///     neither of the runtimes it serves was measured.
        /// </remarks>
        internal static bool IsValueType(this Type type)
        {
#if NET8_0_OR_GREATER
            return type.IsValueType;
#else
            return ValueTypes.GetOrAdd(type, t => t.IsValueType);
#endif
        }

        /// <summary>
        ///     Gets a value indicating whether the member info have the same metadata token.
        /// </summary>
        /// <param name="x">
        ///     The first member info.
        /// </param>
        /// <param name="y">
        ///     The second member info.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the member info have the same metadata token, otherwise <c>false</c>.
        /// </returns>
        private static bool SameMetadataToken(MemberInfo x, MemberInfo y)
        {
            return x.Module.Assembly == y.Module.Assembly && x.Module == y.Module && x.MetadataToken == y.MetadataToken;
        }
    }
}
