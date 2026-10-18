namespace Hyperlite.Interop
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Turns a connection string plus a caller supplied property dictionary into the
    /// resolved property set a context is built from.
    /// </summary>
    /// <remarks>
    /// Replaces ht4c::Context::mergeProperties and the Policies::init hook that followed
    /// it. Precedence, lowest to highest: registered defaults, then a --config file, then
    /// the caller's dictionary, then the connection string. Both spellings of an aliased
    /// property end up present with the same value, which is what callers reading
    /// IContext.Properties rely on.
    /// </remarks>
    internal static class PropertyResolver
    {
        /// <summary>
        /// Resolves the effective properties.
        /// </summary>
        /// <param name="connectionString">Connection string, or null.</param>
        /// <param name="properties">Caller supplied properties, or null.</param>
        /// <returns>The resolved property set.</returns>
        /// <exception cref="ArgumentException">A value does not parse, a provider is
        /// missing or unknown, or a uri cannot be turned into a filename.</exception>
        internal static IDictionary<string, object> Resolve(
            string connectionString,
            IDictionary<string, object> properties)
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal);

            // names whose value is still the registered default, which set_file_uri and
            // the provider hooks below key off
            var defaulted = new HashSet<string>(StringComparer.Ordinal);

            foreach (var descriptor in ConfigRegistry.All)
            {
                if (descriptor.DefaultValue == null)
                {
                    continue;
                }

                values[descriptor.Name] = descriptor.DefaultValue;
                defaulted.Add(descriptor.Name);
            }

            var arguments = ConnectionString.ToArguments(connectionString);

            // a --config file is read first, so both the caller's dictionary and the rest
            // of the connection string still override it
            var configFile = TakeConfigFile(arguments);
            if (configFile != null)
            {
                ApplyConfigFile(configFile, values, defaulted);
            }

            if (properties != null)
            {
                foreach (var item in properties)
                {
                    if (string.IsNullOrEmpty(item.Key))
                    {
                        throw new ArgumentException("Invalid property name", nameof(properties));
                    }

                    Assign(values, defaulted, item.Key, item.Value);
                }
            }

            ApplyArguments(arguments, values, defaulted);

            SyncAliases(values);

            var providerName = RequireProviderName(values);

            if (string.Equals(providerName, Config.ProviderSQLite, StringComparison.Ordinal))
            {
                SetFileUri(values, defaulted, Config.SQLiteFilename);
            }

            SyncAliases(values);
            return values;
        }

        /// <summary>
        /// The provider name, which every context needs.
        /// </summary>
        internal static string RequireProviderName(IDictionary<string, object> values)
        {
            object value;
            if (!values.TryGetValue(Config.ProviderName, out value))
            {
                throw new ArgumentException("Missing provider property", "properties");
            }

            var providerName = value as string;
            if (string.IsNullOrEmpty(providerName))
            {
                throw new ArgumentException("Invalid provider property", "properties");
            }

            return providerName;
        }

        /// <summary>
        /// Copies every aliased value across so both spellings are present.
        /// </summary>
        internal static void SyncAliases(IDictionary<string, object> values)
        {
            foreach (var descriptor in ConfigRegistry.All)
            {
                Sync(values, descriptor.Name, descriptor.Alias);

                // and the pre-rename spelling, on the same terms: whichever spelling the
                // caller supplied seeds the other, so reading a property back under
                // either prefix works
                Sync(values, descriptor.Name, Config.ToLegacyName(descriptor.Name));
            }
        }

        /// <summary>
        /// Copies a value between two spellings of one property, in whichever direction
        /// there is a value to copy.
        /// </summary>
        private static void Sync(IDictionary<string, object> values, string name, string other)
        {
            if (other == null)
            {
                return;
            }

            object value;
            if (values.TryGetValue(name, out value))
            {
                values[other] = value;
            }
            else if (values.TryGetValue(other, out value))
            {
                values[name] = value;
            }
        }

        /// <summary>
        /// Removes a leading "--config &lt;path&gt;" pair from the arguments and returns the path.
        /// </summary>
        private static string TakeConfigFile(List<string> arguments)
        {
            for (var i = 0; i < arguments.Count; ++i)
            {
                var name = Unprefix(arguments[i]);
                string inlineValue = null;

                var equals = name.IndexOf('=');
                if (equals >= 0)
                {
                    inlineValue = name.Substring(equals + 1);
                    name = name.Substring(0, equals);
                }

                if (!string.Equals(name, "config", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (inlineValue != null)
                {
                    arguments.RemoveAt(i);
                    return inlineValue;
                }

                if (i + 1 < arguments.Count)
                {
                    var path = arguments[i + 1];
                    arguments.RemoveRange(i, 2);
                    return path;
                }

                arguments.RemoveAt(i);
                return null;
            }

            return null;
        }

        /// <summary>
        /// Reads a "name=value" per line configuration file. Blank lines and lines
        /// starting with '#' or ';' are comments.
        /// </summary>
        private static void ApplyConfigFile(
            string path,
            IDictionary<string, object> values,
            HashSet<string> defaulted)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!File.Exists(path))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "Configuration file '{0}' does not exist", path));
            }

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                {
                    continue;
                }

                var equals = line.IndexOf('=');
                if (equals < 0)
                {
                    // a bare name is a switch, which for a bool means true
                    AssignText(values, defaulted, line, string.Empty);
                    continue;
                }

                var name = line.Substring(0, equals).Trim();
                var text = line.Substring(equals + 1).Trim();
                AssignText(values, defaulted, name, Unquote(text));
            }
        }

        /// <summary>
        /// Applies the tokenised connection string.
        /// </summary>
        private static void ApplyArguments(
            List<string> arguments,
            IDictionary<string, object> values,
            HashSet<string> defaulted)
        {
            for (var i = 0; i < arguments.Count; ++i)
            {
                var token = arguments[i];
                if (token.Length == 0)
                {
                    continue;
                }

                var name = Unprefix(token);

                // "name=value" and "name value" both occur, and mix within one string
                var equals = name.IndexOf('=');
                if (equals >= 0)
                {
                    var text = name.Substring(equals + 1);
                    name = name.Substring(0, equals);
                    AssignText(values, defaulted, name, text);
                    continue;
                }

                if (i + 1 < arguments.Count && !IsOption(arguments[i + 1]))
                {
                    AssignText(values, defaulted, name, arguments[i + 1]);
                    ++i;
                    continue;
                }

                AssignText(values, defaulted, name, string.Empty);
            }
        }

        /// <summary>
        /// True when the token looks like the next option rather than a value.
        /// </summary>
        private static bool IsOption(string token)
        {
            return token.Length > 1 && token[0] == '-' && token[1] == '-';
        }

        private static string Unprefix(string token)
        {
            if (token.Length > 1 && token[0] == '-' && token[1] == '-')
            {
                return token.Substring(2);
            }

            if (token.Length > 0 && token[0] == '-')
            {
                return token.Substring(1);
            }

            return token;
        }

        private static string Unquote(string text)
        {
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            {
                return text.Substring(1, text.Length - 2);
            }

            return text;
        }

        private static void AssignText(
            IDictionary<string, object> values,
            HashSet<string> defaulted,
            string name,
            string text)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var descriptor = ConfigRegistry.Find(name);
            var value = ConfigRegistry.Convert(descriptor, text);
            Assign(values, defaulted, descriptor != null ? descriptor.Name : name, value);

            if (descriptor != null && descriptor.Alias != null)
            {
                values[descriptor.Alias] = value;
            }
        }

        private static void Assign(
            IDictionary<string, object> values,
            HashSet<string> defaulted,
            string name,
            object value)
        {
            var descriptor = ConfigRegistry.Find(name);
            var canonical = descriptor != null ? descriptor.Name : name;

            // a caller supplied string for a typed property still has to be converted,
            // otherwise Properties would report "32" where ht4n reported 32
            if (descriptor != null && value is string && descriptor.Kind != PropertyKind.String)
            {
                value = ConfigRegistry.Convert(descriptor, (string)value);
            }

            values[canonical] = value;
            defaulted.Remove(canonical);

            if (descriptor != null && descriptor.Alias != null)
            {
                values[descriptor.Alias] = value;
                defaulted.Remove(descriptor.Alias);
            }
        }

        /// <summary>
        /// Derives the database filename from the uri.
        /// </summary>
        /// <remarks>
        /// Reproduces ht4c's set_file_uri: an explicitly given uri wins, otherwise the uri
        /// is only consulted when the filename is absent or still its default. That is
        /// what lets "--Provider SQLite --Ht4n.SQLite.Filename x" work without a uri.
        /// </remarks>
        private static void SetFileUri(
            IDictionary<string, object> values,
            HashSet<string> defaulted,
            string filenameProperty)
        {
            var uriIsExplicit = !defaulted.Contains(Config.Uri);
            var filenameMissing = !values.ContainsKey(filenameProperty)
                                  || defaulted.Contains(filenameProperty);

            if (!uriIsExplicit && !filenameMissing)
            {
                return;
            }

            object uriValue;
            values.TryGetValue(Config.Uri, out uriValue);
            var uri = uriValue as string ?? string.Empty;

            uri = ExpandEnvironment(uri.Trim()).Trim();
            values[Config.Uri] = uri;
            values[Config.UriAlias] = uri;

            values[filenameProperty] = UriToFilename(uri);
            defaulted.Remove(filenameProperty);
        }

        /// <summary>
        /// Expands %NAME% style environment references, as ht4c did with
        /// ExpandEnvironmentStrings.
        /// </summary>
        internal static string ExpandEnvironment(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('%') < 0)
            {
                return text;
            }

            return Environment.ExpandEnvironmentVariables(text);
        }

        /// <summary>
        /// Turns a file:// uri into a local path.
        /// </summary>
        /// <remarks>
        /// Carried over from ht4c's uri_to_filename, including the drive letter handling
        /// that makes "file:///c/Temp/x" and "file:///c:/Temp/x" both come out as
        /// "c:\Temp\x", and the percent decoding that follows the separator rewrite. Both
        /// on Windows only: elsewhere "file:///tmp/x" is "/tmp/x".
        /// </remarks>
        internal static string UriToFilename(string uri)
        {
            const string Scheme = "file://";

            if (uri == null || !uri.StartsWith(Scheme, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid uri scheme, file:///[drive][/path/]uri required");
            }

            var windows = Path.DirectorySeparatorChar == '\\';
            var absolute = uri.StartsWith("file:///", StringComparison.Ordinal);
            var path = uri.Substring(Scheme.Length);

            if (windows && absolute && path.Length >= 3 && char.IsLetter(path[1]) && (path[2] == ':' || path[2] == '/'))
            {
                path = path.TrimStart('/');
                if (path.Length > 1 && path[1] != ':')
                {
                    path = path.Substring(0, 1) + ":" + path.Substring(1);
                }
            }

            if (path.Length == 0)
            {
                throw new ArgumentException("Empty uri, file:///[drive][/path/]uri required");
            }

            if (windows)
            {
                path = ReplaceAllRecursive(path, "///", "//");
                path = ReplaceAllRecursive(path, "/", "\\");
            }
            else
            {
                path = ReplaceAllRecursive(path, "//", "/");
            }

            return UriDecode(path);
        }

        /// <summary>
        /// Decodes %XX escapes. Anything else is passed through untouched.
        /// </summary>
        private static string UriDecode(string encoded)
        {
            if (encoded.IndexOf('%') < 0)
            {
                return encoded;
            }

            var decoded = new StringBuilder(encoded.Length);
            for (var i = 0; i < encoded.Length; ++i)
            {
                if (encoded[i] == '%'
                    && i + 2 < encoded.Length
                    && IsHexDigit(encoded[i + 1])
                    && IsHexDigit(encoded[i + 2]))
                {
                    var value = System.Convert.ToInt32(encoded.Substring(i + 1, 2), 16);
                    decoded.Append((char)value);
                    i += 2;
                }
                else
                {
                    decoded.Append(encoded[i]);
                }
            }

            return decoded.ToString();
        }

        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static string ReplaceAllRecursive(string input, string search, string replacement)
        {
            string previous;
            do
            {
                previous = input;
                input = input.Replace(search, replacement);
            }
            while (previous != input);

            return input;
        }
    }
}
