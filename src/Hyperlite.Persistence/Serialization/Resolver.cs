namespace Hyperlite.Persistence.Serialization
{
    using System;
    using System.Linq;
    using System.Reflection;

    public static class Resolver
    {
        private static Func<AssemblyName, Assembly> assemblyResolver;

        private static Func<Type, Type, object> instanceResolver;

        private static Action<object, string, object> obsoletePropertyResolver;

        private static Func<Type, string, string> propertyNameResolver;

        private static Func<int, Type> typeCodeResolver;

        private static Func<Assembly, string, bool, Type> typeResolver;

        /// <summary>
        ///     Initializes static members of the <see cref="Resolver" /> class.
        /// </summary>
        static Resolver()
        {
            assemblyResolver = assemblyName => AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name));
            typeResolver = (assembly, simpleTypeName, ignoreCase) =>
                assembly.GetType(simpleTypeName, false, ignoreCase);
            instanceResolver = (serializedType, destinationType) => null;
            obsoletePropertyResolver = (instance, proertyName, value) => { };
        }

        public static Func<AssemblyName, Assembly> AssemblyResolver
        {
            get { return assemblyResolver; }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                assemblyResolver = value;
            }
        }

        public static Func<Type, Type, object> InstanceResolver
        {
            get { return instanceResolver; }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                instanceResolver = value;
            }
        }

        public static Action<object, string, object> ObsoletePropertyResolver
        {
            get { return obsoletePropertyResolver; }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                obsoletePropertyResolver = value;
            }
        }

        /// <summary>
        ///     Null, the default, is no resolver; setting it back to null removes one.
        /// </summary>
        public static Func<Type, string, string> PropertyNameResolver
        {
            get { return propertyNameResolver; }

            set { propertyNameResolver = value; }
        }

        public static Func<int, Type> TypeCodeResolver
        {
            get { return typeCodeResolver; }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                typeCodeResolver = value;
            }
        }

        public static Func<Assembly, string, bool, Type> TypeResolver
        {
            get { return typeResolver; }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                typeResolver = value;
            }
        }
    }
}
