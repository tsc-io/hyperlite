namespace Hyperlite.Persistence.Reflection
{
    using System;
    using Hyperlite.Persistence.Serialization;

    public static class TypeLoader
    {
        /// <summary>
        ///     A framework dictionary and its randomized string hash, deliberately, not the
        ///     library's <c>ConcurrentStringDictionary</c>: the keys are type names read from
        ///     serialized bytes, so they come from outside, and a non-randomized hash could be
        ///     flooded with names chosen to collide. See <c>InternalStringComparer</c>.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Type> Types =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Type>();

        /// <summary>
        ///     Gets type for the type name specified.
        /// </summary>
        /// <returns>
        ///     The resolved type.
        /// </returns>
        /// <remarks>
        ///     <para>
        ///         Under strict types a name resolves only against the assemblies the process
        ///         has loaded - through <see cref="Resolver.AssemblyResolver" />, which by default
        ///         does exactly that - and never loads one by name: the name comes from the bytes,
        ///         and loading an assembly runs its code before strict types could refuse
        ///         anything. Every type strict types allow is in a loaded assembly already: a
        ///         declared or allowed type is one the application holds, and so are the
        ///         framework's. Off, a name may load the assembly it names, as it always could.
        ///     </para>
        ///     <para>
        ///         Only a name that resolves is cached. A miss used to be cached as well, and the
        ///         names come from the bytes, so a stream of distinct names grew the cache for the
        ///         life of the process; what is cached now is bounded by the types there are.
        ///     </para>
        /// </remarks>
        public static Type GetType(string typeName)
        {
            Type type;
            if (Types.TryGetValue(typeName, out type))
            {
                return type;
            }

            type = Resolve(typeName, Encoder.Configuration.StrictTypes);
            if (type != null)
            {
                Types.TryAdd(typeName, type);
            }

            return type;
        }

        private static Type Resolve(string typeName, bool loadedAssembliesOnly)
        {
            // a failure to load or to parse is a type that does not resolve, nothing more:
            // generic arguments qualified with an assembly version that predates the one
            // available are one way to get there
            if (!loadedAssembliesOnly)
            {
                try
                {
                    var type = Type.GetType(typeName, false, false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                }
            }

            try
            {
                return Type.GetType(typeName, Resolver.AssemblyResolver, ResolveType);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        ///     The type resolver, with a name that names no assembly looked up where the runtime
        ///     looks - the core library and the calling assembly - which loads nothing.
        /// </summary>
        private static Type ResolveType(System.Reflection.Assembly assembly, string typeName, bool ignoreCase)
        {
            return assembly != null
                ? Resolver.TypeResolver(assembly, typeName, ignoreCase)
                : Type.GetType(typeName, false, ignoreCase);
        }
    }
}
