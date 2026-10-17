namespace Hyperlite.Interop
{
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Splits a connection string into arguments.
    /// </summary>
    /// <remarks>
    /// Replaces ht4c::Context::commandLineToArgv. Two spellings have to work, and mix:
    ///
    ///   "Provider=SQLite; Uri=file://test.db"      semicolon separated assignments
    ///   "--Provider SQLite --Ht4n.SQLite.Filename x"   command line style
    ///
    /// The original normalised the semicolon form into the command line form by turning
    /// every ';' into ";--" and prefixing a leading "--", then ran a quote aware
    /// tokeniser over the result. That is reproduced here, including the detail that a
    /// ';' inside quotes is kept literally and the injected "--" skipped again - which is
    /// what lets an ODBC connection string full of semicolons survive being quoted.
    /// </remarks>
    internal static class ConnectionString
    {
        /// <summary>
        /// Tokenises a connection string.
        /// </summary>
        /// <param name="connectionString">Connection string, may be null or empty.</param>
        /// <returns>The arguments, without the leading program name the original carried.</returns>
        internal static List<string> ToArguments(string connectionString)
        {
            var arguments = new List<string>();
            if (string.IsNullOrEmpty(connectionString))
            {
                return arguments;
            }

            var normalized = Normalize(connectionString);
            if (normalized.Length == 0)
            {
                return arguments;
            }

            var current = new StringBuilder();
            var inQuotes = false;
            var inText = false;

            for (var i = 0; i < normalized.Length; ++i)
            {
                var c = normalized[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        inQuotes = false;
                    }
                    else if (c == ';')
                    {
                        // a quoted semicolon is literal, so step over the "--" that
                        // Normalize injected after it
                        current.Append(c);
                        i += 2;
                    }
                    else
                    {
                        current.Append(c);
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        inText = true;
                        break;

                    case ' ':
                    case ';':
                    case '\t':
                    case '\n':
                    case '\r':
                        if (inText)
                        {
                            arguments.Add(current.ToString());
                            current.Length = 0;
                        }

                        inText = false;
                        break;

                    default:
                        inText = true;
                        current.Append(c);
                        break;
                }
            }

            if (inText || current.Length > 0)
            {
                arguments.Add(current.ToString());
            }

            return arguments;
        }

        /// <summary>
        /// Turns the semicolon form into the command line form.
        /// </summary>
        private static string Normalize(string connectionString)
        {
            var text = connectionString.Trim();

            // collapse whitespace around separators and runs of separators
            text = ReplaceAllRecursive(text, " ;", ";");
            text = ReplaceAllRecursive(text, "; ", ";");
            text = ReplaceAllRecursive(text, ";;", ";");
            text = text.Trim(';');

            text = text.Replace(";", ";--");
            text = text.Trim();

            if (text.Length > 0 && text[0] != '-' && text[0] != ';')
            {
                text = "--" + text;
            }

            return text;
        }

        /// <summary>
        /// Replaces until nothing changes, so overlapping matches collapse too.
        /// </summary>
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
