namespace Hyperlite.Interop
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// The type a known configuration property carries.
    /// </summary>
    internal enum PropertyKind
    {
        String,

        Int32,

        UInt16,

        Int64,

        Double,

        Boolean,

        StringList
    }

    /// <summary>
    /// One known configuration property.
    /// </summary>
    internal sealed class PropertyDescriptor
    {
        internal PropertyDescriptor(string name, PropertyKind kind, object defaultValue, string alias)
        {
            this.Name = name;
            this.Kind = kind;
            this.DefaultValue = defaultValue;
            this.Alias = alias;
        }

        internal string Name { get; private set; }

        internal PropertyKind Kind { get; private set; }

        /// <summary>
        /// The value the property takes when nothing sets it, or null when it has none.
        /// </summary>
        internal object DefaultValue { get; private set; }

        /// <summary>
        /// The short spelling, or null. Both spellings end up in the property set with the
        /// same value.
        /// </summary>
        internal string Alias { get; private set; }
    }

    /// <summary>
    /// The known configuration properties, their types, their defaults and their aliases.
    /// </summary>
    /// <remarks>
    /// Replaces the boost::program_options descriptions ht4c::Context registered. Only
    /// the properties of the providers Hyperlite actually builds are listed; anything else a
    /// caller supplies is carried through untyped, which is what
    /// allow_unregistered_options(true) did.
    ///
    /// The defaults are the ht4c defaults, and callers can read them back through
    /// IContext.Properties, so they are part of the observable contract.
    /// </remarks>
    internal static class ConfigRegistry
    {
        /// <summary>
        /// The provider Hyperlite defaults to. SQLite is the only one built.
        /// </summary>
        internal const string DefaultProvider = Config.ProviderSQLite;

        /// <summary>
        /// The uri a caller gets when none is given.
        /// </summary>
        internal const string DefaultUri = "net.tcp://localhost";

        /// <summary>
        /// Connection timeout default, in milliseconds.
        /// </summary>
        internal const int DefaultConnectionTimeout = 30000;

        private static readonly PropertyDescriptor[] Descriptors =
        {
            new PropertyDescriptor(Config.ProviderName, PropertyKind.String, DefaultProvider, Config.ProviderNameAlias),
            new PropertyDescriptor(Config.Uri, PropertyKind.String, DefaultUri, Config.UriAlias),
            new PropertyDescriptor(Config.ConnectionTimeout, PropertyKind.Int32, DefaultConnectionTimeout, Config.ConnectionTimeoutAlias),

            new PropertyDescriptor(Config.SQLiteFilename, PropertyKind.String, null, null),
            new PropertyDescriptor(Config.SQLiteCacheSizeMB, PropertyKind.Int32, 64, null),
            new PropertyDescriptor(Config.SQLitePageSizeKB, PropertyKind.Int32, 4, null),
            new PropertyDescriptor(Config.SQLiteWriteAheadLog, PropertyKind.Boolean, false, null),
            new PropertyDescriptor(Config.SQLiteSynchronous, PropertyKind.Boolean, false, null),
            new PropertyDescriptor(Config.SQLiteAutoVacuum, PropertyKind.Int32, 0, null),
            new PropertyDescriptor(Config.SQLiteUniqueRows, PropertyKind.Boolean, false, null),
            new PropertyDescriptor(Config.SQLiteNoCellRevisions, PropertyKind.Boolean, false, null),
            new PropertyDescriptor(Config.SQLiteIndexColumn, PropertyKind.Boolean, false, null),
            new PropertyDescriptor(Config.SQLiteIndexColumnFamily, PropertyKind.Boolean, false, null),
            new PropertyDescriptor(Config.SQLiteIndexColumnQualifier, PropertyKind.Boolean, false, null),
            new PropertyDescriptor(Config.SQLiteIndexTimestamp, PropertyKind.Boolean, false, null)
        };

        private static readonly Dictionary<string, PropertyDescriptor> ByName = BuildByName();

        /// <summary>
        /// Every known property, in registration order.
        /// </summary>
        internal static IEnumerable<PropertyDescriptor> All
        {
            get
            {
                return Descriptors;
            }
        }

        /// <summary>
        /// Looks a property up by its full name or its alias.
        /// </summary>
        /// <returns>The descriptor, or null when the property is not known.</returns>
        internal static PropertyDescriptor Find(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            PropertyDescriptor descriptor;
            return ByName.TryGetValue(name, out descriptor) ? descriptor : null;
        }

        /// <summary>
        /// Converts a textual value to the property's type.
        /// </summary>
        /// <exception cref="ArgumentException">The text does not parse as that type.</exception>
        internal static object Convert(PropertyDescriptor descriptor, string text)
        {
            if (descriptor == null)
            {
                // unregistered, so it stays a string
                return text;
            }

            try
            {
                switch (descriptor.Kind)
                {
                    case PropertyKind.Boolean:
                        return ParseBoolean(text);
                    case PropertyKind.Int32:
                        return int.Parse(text, CultureInfo.InvariantCulture);
                    case PropertyKind.UInt16:
                        return ushort.Parse(text, CultureInfo.InvariantCulture);
                    case PropertyKind.Int64:
                        return long.Parse(text, CultureInfo.InvariantCulture);
                    case PropertyKind.Double:
                        return double.Parse(text, CultureInfo.InvariantCulture);
                    case PropertyKind.StringList:
                        return new List<string> { text };
                    default:
                        return text;
                }
            }
            catch (Exception e)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Invalid value '{0}' for property '{1}'",
                        text,
                        descriptor.Name),
                    e);
            }
        }

        /// <summary>
        /// Accepts the spellings boost::program_options accepted for a bool.
        /// </summary>
        internal static bool ParseBoolean(string text)
        {
            if (text == null)
            {
                throw new FormatException("Missing boolean value");
            }

            var trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                // a bare switch means true, as it did on the command line
                return true;
            }

            switch (trimmed.ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
                default:
                    throw new FormatException("Invalid boolean value '" + text + "'");
            }
        }

        private static Dictionary<string, PropertyDescriptor> BuildByName()
        {
            // ordinal, because the names are identifiers rather than prose
            var map = new Dictionary<string, PropertyDescriptor>(StringComparer.Ordinal);
            foreach (var descriptor in Descriptors)
            {
                map[descriptor.Name] = descriptor;
                if (descriptor.Alias != null)
                {
                    map[descriptor.Alias] = descriptor;
                }

                // the pre-rename spelling, so a connection string or a configuration file
                // written against the Ht4n. prefix is still recognised - and recognised
                // means typed: an unknown name is carried through as text, so a caller
                // asking for an int would not get one back
                var legacy = Config.ToLegacyName(descriptor.Name);
                if (legacy != null)
                {
                    map[legacy] = descriptor;
                }
            }

            return map;
        }
    }
}
