namespace Hyperlite.Persistence.Serialization
{
    using System;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Text.RegularExpressions;
    using Hyperlite.Persistence.Reflection;

    public class Binder : SerializationBinder
    {
        private static readonly Regex Culture = new Regex(@", Culture=\w+", RegexOptions.Compiled);

        private static readonly Regex PublicKeyToken = new Regex(@", PublicKeyToken=\w+", RegexOptions.Compiled);

        private static readonly Regex Version = new Regex(@", Version=\d+.\d+.\d+.\d+", RegexOptions.Compiled);

        /// <summary>
        ///     The core library's simple name on this runtime.
        /// </summary>
        private static readonly string LocalCoreLibrary = typeof(object).Assembly.GetName().Name;

        /// <summary>
        ///     Every simple name the core library goes by, across runtimes.
        /// </summary>
        private static readonly string[] CoreLibraryNames = { "mscorlib", "System.Private.CoreLib" };

        /// <summary>
        ///     The core library's name together with the public key token that follows it,
        ///     which differs per runtime and has to go with the name.
        /// </summary>
        private static readonly Regex CoreLibraryToken = new Regex(
            @"(?:mscorlib|System\.Private\.CoreLib), PublicKeyToken=\w+", RegexOptions.Compiled);

        private static bool removeAssemblyCulture = true;

        private static bool removeAssemblyVersion = true;

        /// <summary>
        ///     Gets or sets a value indicating whether the assembly culture should be removed from the qualified type name.
        /// </summary>
        /// <value>
        ///     The strict explicit type codes.
        /// </value>
        public static bool RemoveAssemblyCulture
        {
            get { return removeAssemblyCulture; }

            set { removeAssemblyCulture = value; }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether the assembly public key token should be removed from the qualified type
        ///     name.
        /// </summary>
        /// <value>
        ///     The strict explicit type codes.
        /// </value>
        public static bool RemoveAssemblyPublicKeyToken { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether the assembly version should be removed from the qualified type name.
        /// </summary>
        /// <value>
        ///     The strict explicit type codes.
        /// </value>
        public static bool RemoveAssemblyVersion
        {
            get { return removeAssemblyVersion; }

            set { removeAssemblyVersion = value; }
        }

        public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            if (serializedType == null)
            {
                throw new ArgumentNullException(nameof(serializedType));
            }

            assemblyName = serializedType.Assembly.FullName;
            typeName = serializedType.FullName ?? serializedType.Name;

            if (RemoveAssemblyVersion)
            {
                if (assemblyName != null)
                {
                    assemblyName = Version.Replace(assemblyName, string.Empty);
                }

                typeName = Version.Replace(typeName, string.Empty);
            }

            if (RemoveAssemblyCulture)
            {
                if (assemblyName != null)
                {
                    assemblyName = Culture.Replace(assemblyName, string.Empty);
                }

                typeName = Culture.Replace(typeName, string.Empty);
            }

            if (RemoveAssemblyPublicKeyToken)
            {
                if (assemblyName != null)
                {
                    assemblyName = PublicKeyToken.Replace(assemblyName, string.Empty);
                }

                typeName = PublicKeyToken.Replace(typeName, string.Empty);
            }
        }

        /// <summary>
        ///     When overridden in a derived class, controls the binding of a serialized object to a type.
        /// </summary>
        /// <returns>
        ///     The type of the object the formatter creates a new instance of.
        /// </returns>
        /// <param name="assemblyName">
        ///     Specifies the <see cref="T:System.Reflection.Assembly" /> name of the serialized object.
        /// </param>
        /// <param name="typeName">
        ///     Specifies the <see cref="T:System.Type" /> name of the serialized object.
        /// </param>
        public override Type BindToType(string assemblyName, string typeName)
        {
            var type = TypeLoader.GetType(Assembly.CreateQualifiedName(assemblyName, typeName));
            if (type != null)
            {
                return type;
            }

            // Nothing resolved, so try again with the core library renamed.
            //
            // The core library has a different identity on each runtime - mscorlib on
            // .NET Framework, System.Private.CoreLib on .NET - and it is written into the
            // stored name of every type that lives there, including the generic arguments
            // of one that does not. So a Type, or any value that records its concrete type,
            // stored by a .NET process did not resolve at all in a .NET Framework one:
            // BindToType returned null and the property came back empty rather than
            // failing, which is the worse of the two.
            //
            // The other direction already worked, because mscorlib is type-forwarded on
            // .NET. This makes it work both ways.
            //
            // Only reached after the name as stored has failed, so no resolution that
            // succeeds today can change, and nothing about what is written changes.
            var remapped = RemapCoreLibrary(Assembly.CreateQualifiedName(assemblyName, typeName));
            return remapped == null ? null : TypeLoader.GetType(remapped);
        }

        /// <summary>
        ///     Rewrites the other runtime's core library name to this one's, or returns
        ///     null when the name does not mention it.
        /// </summary>
        private static string RemapCoreLibrary(string qualifiedName)
        {
            if (qualifiedName == null)
            {
                return null;
            }

            var remapped = qualifiedName;
            foreach (var foreign in CoreLibraryNames)
            {
                if (string.Equals(foreign, LocalCoreLibrary, StringComparison.Ordinal))
                {
                    continue;
                }

                remapped = remapped.Replace(foreign, LocalCoreLibrary);
            }

            // the public key token travels with the name and does not match the local
            // core library's, so drop the one that follows it
            remapped = CoreLibraryToken.Replace(remapped, LocalCoreLibrary);

            return string.Equals(remapped, qualifiedName, StringComparison.Ordinal) ? null : remapped;
        }
    }
}
