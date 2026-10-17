namespace Hyperlite.Interop
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using System.Runtime.ExceptionServices;

    /// <summary>
    /// The native sqlite environment a context owns.
    /// </summary>
    /// <remarks>
    /// One per context. The clients below it borrow this handle, and the native objects they
    /// create hold the native environment themselves, so it stays open for them after the
    /// context has been disposed, see <see cref="IContext" />.
    /// </remarks>
    internal sealed unsafe class EnvironmentHandle : IDisposable
    {
        private readonly object sync = new object();

        private IntPtr handle;

        private EnvironmentHandle(IntPtr handle)
        {
            this.handle = handle;
        }

        /// <summary>
        /// The native handle. Only valid while the lock is held: every call on it is made under
        /// the lock, so that disposing the context cannot close the environment under a call.
        /// </summary>
        private IntPtr Handle
        {
            get
            {
                var current = this.handle;
                if (current == IntPtr.Zero)
                {
                    throw new ObjectDisposedException(nameof(Context));
                }

                return current;
            }
        }

        /// <summary>
        /// Opens the database the properties describe.
        /// </summary>
        internal static EnvironmentHandle Open(IDictionary<string, object> properties)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            var filename = Require(properties, Config.SQLiteFilename);

            LoadNative();

            // start from the native defaults so anything the caller left out matches ht4c
            var config = Native.DefaultConfig();

            config.CacheSizeMB = Int32Or(properties, Config.SQLiteCacheSizeMB, config.CacheSizeMB);
            config.PageSizeKB = Int32Or(properties, Config.SQLitePageSizeKB, config.PageSizeKB);
            config.AutoVacuum = Int32Or(properties, Config.SQLiteAutoVacuum, config.AutoVacuum);
            config.WriteAheadLog = BoolOr(properties, Config.SQLiteWriteAheadLog, config.WriteAheadLog != 0) ? 1 : 0;
            config.Synchronous = BoolOr(properties, Config.SQLiteSynchronous, config.Synchronous != 0) ? 1 : 0;
            config.UniqueRows = BoolOr(properties, Config.SQLiteUniqueRows, config.UniqueRows != 0) ? 1 : 0;
            config.NoCellRevisions = BoolOr(properties, Config.SQLiteNoCellRevisions, config.NoCellRevisions != 0) ? 1 : 0;
            config.IndexColumn = BoolOr(properties, Config.SQLiteIndexColumn, config.IndexColumn != 0) ? 1 : 0;
            config.IndexColumnFamily = BoolOr(properties, Config.SQLiteIndexColumnFamily, config.IndexColumnFamily != 0) ? 1 : 0;
            config.IndexColumnQualifier = BoolOr(properties, Config.SQLiteIndexColumnQualifier, config.IndexColumnQualifier != 0) ? 1 : 0;
            config.IndexTimestamp = BoolOr(properties, Config.SQLiteIndexTimestamp, config.IndexTimestamp != 0) ? 1 : 0;

            var bytes = Utf8.Encode(filename);
            IntPtr created;
            fixed (byte* pinned = bytes)
            {
                Native.Check(Native.Table.EnvGetOrCreate(pinned, bytes.Length - 1, &config, &created));
            }

            return new EnvironmentHandle(created);
        }

        /// <summary>
        /// VACUUMs the database.
        /// </summary>
        internal void Optimize()
        {
            lock (this.sync)
            {
                Native.Check(Native.Table.EnvOptimize(this.Handle));
            }
        }

        /// <summary>
        /// Creates a native client on this environment.
        /// </summary>
        internal IntPtr CreateClient()
        {
            lock (this.sync)
            {
                IntPtr created;
                Native.Check(Native.Table.CliCreate(this.Handle, &created));
                return created;
            }
        }

        public void Dispose()
        {
            IntPtr released;
            lock (this.sync)
            {
                released = this.handle;
                this.handle = IntPtr.Zero;
            }

            if (released != IntPtr.Zero)
            {
                Native.Check(Native.Table.EnvClose(released));
            }
        }

        /// <summary>
        /// Binds the native companion, surfacing why it could not be: a failure in a static
        /// constructor reaches the caller as a <see cref="TypeInitializationException" />, the
        /// DllNotFoundException or TypeLoadException that says what is wrong inside it.
        /// </summary>
        private static void LoadNative()
        {
            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(Native).TypeHandle);
            }
            catch (TypeInitializationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            }
        }

        private static string Require(IDictionary<string, object> properties, string name)
        {
            object value;
            if (!properties.TryGetValue(name, out value))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "Missing '{0}' property", name), nameof(properties));
            }

            var text = value as string;
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "Invalid '{0}' property", name), nameof(properties));
            }

            return text;
        }

        private static int Int32Or(IDictionary<string, object> properties, string name, int fallback)
        {
            object value;
            if (!properties.TryGetValue(name, out value) || value == null)
            {
                return fallback;
            }

            if (value is int)
            {
                return (int)value;
            }

            var text = value as string;
            if (text != null)
            {
                int parsed;
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }

            return System.Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static bool BoolOr(IDictionary<string, object> properties, string name, bool fallback)
        {
            object value;
            if (!properties.TryGetValue(name, out value) || value == null)
            {
                return fallback;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            var text = value as string;
            if (text != null)
            {
                return ConfigRegistry.ParseBoolean(text);
            }

            return System.Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
    }
}
