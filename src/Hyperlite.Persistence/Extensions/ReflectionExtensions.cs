namespace Hyperlite.Persistence.Extensions
{
    using System;
    using System.Reflection;
    using System.Runtime.Serialization;

    internal static class ReflectionExtensions
    {
        /// <summary>
        ///     Gets a single attribute from the member info specified.
        /// </summary>
        /// <typeparam name="T">
        ///     The attribute type.
        /// </typeparam>
        /// <returns>
        ///     The attribute instance or null.
        /// </returns>
        internal static T GetAttribute<T>(this MemberInfo memberInfo) where T : Attribute
        {
            var attributes = memberInfo.GetCustomAttributes(typeof(T), false);
            return attributes.Length > 0 ? (T)attributes[0] : null;
        }

        /// <summary>
        ///     Gets a single attribute from the assembly specified.
        /// </summary>
        /// <typeparam name="T">
        ///     The attribute type.
        /// </typeparam>
        /// <returns>
        ///     The attribute instance or null.
        /// </returns>
        internal static T GetAttribute<T>(this Assembly assembly) where T : Attribute
        {
            var attributes = assembly.GetCustomAttributes(typeof(T), false);
            return attributes.Length > 0 ? (T)attributes[0] : null;
        }

        /// <summary>
        ///     Returns a value indicating whether the assembly has the attribute declared.
        /// </summary>
        /// <typeparam name="T">
        ///     The attribute type.
        /// </typeparam>
        /// <returns>
        ///     <c>true</c> if the assembly has the attribute declared, otherwise <c>false</c>.
        /// </returns>
        internal static bool HasAttribute<T>(this Assembly assembly) where T : Attribute
        {
            return assembly.GetCustomAttributes(typeof(T), false).Length > 0;
        }

        /// <summary>
        ///     Returns a value indicating whether the member info has the attribute declared.
        /// </summary>
        /// <typeparam name="T">
        ///     The attribute type.
        /// </typeparam>
        /// <returns>
        ///     <c>true</c> if the member info has the attribute declared, otherwise <c>false</c>.
        /// </returns>
        internal static bool HasAttribute<T>(this MemberInfo memberInfo) where T : Attribute
        {
            return memberInfo.GetCustomAttributes(typeof(T), false).Length > 0;
        }

        /// <summary>
        ///     Creates a serializable name for the member info specified.
        /// </summary>
        internal static string SerializableName(this MemberInfo memberInfo)
        {
            var dataMemberAttribute = memberInfo.GetAttribute<DataMemberAttribute>();
            if (!string.IsNullOrEmpty(dataMemberAttribute?.Name))
            {
                return dataMemberAttribute.Name;
            }

            var fieldInfo = memberInfo as FieldInfo;
            return fieldInfo != null ? EscapeFieldInfoName(fieldInfo) : memberInfo.Name;
        }

        /// <returns>
        ///     The escaped field info name.
        /// </returns>
        private static string EscapeFieldInfoName(FieldInfo fieldInfo)
        {
            var name = fieldInfo.Name;
            if (name.StartsWith("<", StringComparison.Ordinal))
            {
                var closure = name.IndexOf('>');
                if (closure > 1)
                {
                    name = name.StartsWith("<backing_store>", StringComparison.Ordinal)
                        ? name.Substring(closure + 1)
                        : name.Substring(1, closure - 1);
                }
            }

            return name;
        }
    }
}
