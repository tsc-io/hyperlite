namespace Hyperlite.Persistence.Reflection
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal static class TypeFinder
    {
        /// <summary>
        ///     Gets the common base type for the types specified.
        /// </summary>
        internal static Type GetCommonBaseType(IEnumerable<Type> types)
        {
            return GetCommonBaseType(types.ToArray());
        }

        /// <summary>
        ///     Gets the common base type for the types specified.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="types" /> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     If <paramref name="types" /> contains a null element.
        /// </exception>
        internal static Type GetCommonBaseType(Type[] types)
        {
            if (types == null)
            {
                throw new ArgumentNullException(nameof(types));
            }

            if (types.Length == 0)
            {
                return typeof(object);
            }

            var commonBaseClass = types[0];
            if (commonBaseClass == null)
            {
                throw new ArgumentException("types contains a null reference", nameof(types));
            }

            for (var i = 1; i < types.Length; ++i)
            {
                var type = types[i];
                if (type == null)
                {
                    throw new ArgumentException("types contains a null reference", nameof(types));
                }

                if (type.IsAssignableFrom(commonBaseClass))
                {
                    commonBaseClass = type;
                }
                else
                {
                    while (commonBaseClass != null && !commonBaseClass.IsAssignableFrom(type))
                    {
                        commonBaseClass = commonBaseClass.BaseType;
                    }
                }
            }

            return commonBaseClass;
        }
    }
}
