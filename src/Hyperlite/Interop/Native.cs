namespace Hyperlite.Interop
{
    using System;

    /// <summary>
    /// Binds the Hyperlite.Native dispatch table and turns native status codes into the
    /// exceptions ht4n raised.
    /// </summary>
    internal static unsafe class Native
    {
        /// <summary>
        /// BAD_SCAN_SPEC, what a pattern that does not compile fails with.
        /// </summary>
        private const int BadScanSpec = 30;

        /// <summary>
        /// The bound dispatch table. Read-only after the static constructor.
        /// </summary>
        internal static readonly Dispatch Table;

        /// <summary>
        /// sqlite version the native companion was built against, for diagnostics.
        /// </summary>
        internal static readonly string SqliteVersion;

        static Native()
        {
            var entry = NativeLoader.LoadEntryPoint();

            var bootstrap = (delegate* unmanaged[Cdecl]<int, Dispatch**, int>)entry;

            Dispatch* table = null;
            var rc = bootstrap(Abi.AbiRevision, &table);
            if (rc > 0)
            {
                // a positive result is the revision the library actually speaks
                throw new TypeLoadException(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Hyperlite.Native speaks ABI revision {0}, Hyperlite expects {1}. " +
                    "The managed and native halves are from different builds.",
                    rc,
                    Abi.AbiRevision));
            }

            if (rc != 0 || table == null)
            {
                throw new TypeLoadException(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Hyperlite.Native handed out no dispatch table (status {0}).",
                    rc));
            }

            Table = *table;

            SqliteVersion = Utf8.ToString(Table.SqliteVersion());

            // the native side owns no regex engine, matching comes back here
            RegexBridge.Install();
        }

        /// <summary>
        /// Throws when the native call failed.
        /// </summary>
        /// <param name="status">Status returned by a dispatch table member.</param>
        internal static void Check(int status)
        {
            if (status == 0)
            {
                return;
            }

            throw BuildException(status, 0);
        }

        /// <summary>
        /// Rebuilds the managed exception, innermost frame first so the inner chain
        /// comes out the way ht4n produced it.
        /// </summary>
        private static Exception BuildException(int status, int depth)
        {
            NativeError error;
            if (Table.ErrGet(depth, &error) == 0)
            {
                // the native side reported a status but no detail, which should not
                // happen; still better than losing the failure
                return HyperliteExceptionFactory.Create(
                    status, "Native failure without detail", 0, string.Empty, string.Empty, null);
            }

            var inner = BuildExceptionIfPresent(depth + 1);
            if (inner == null && depth == 0 && error.Code == BadScanSpec)
            {
                // a pattern that did not compile: the regex parser said why
                inner = RegexBridge.TakeCompileFailure();
            }

            return HyperliteExceptionFactory.Create(
                error.Code,
                Utf8.ToString(error.Text, error.TextLength),
                error.Line,
                Utf8.ToString(error.Func, error.FuncLength),
                Utf8.ToString(error.File, error.FileLength),
                inner);
        }

        private static Exception BuildExceptionIfPresent(int depth)
        {
            NativeError probe;
            if (Table.ErrGet(depth, &probe) == 0)
            {
                return null;
            }

            var inner = BuildExceptionIfPresent(depth + 1);

            return HyperliteExceptionFactory.Create(
                probe.Code,
                Utf8.ToString(probe.Text, probe.TextLength),
                probe.Line,
                Utf8.ToString(probe.Func, probe.FuncLength),
                Utf8.ToString(probe.File, probe.FileLength),
                inner);
        }

        /// <summary>
        /// Reads a native buffer as a string and releases it.
        /// </summary>
        internal static string TakeString(ref NativeBuffer buffer)
        {
            try
            {
                return Utf8.ToString((byte*)buffer.Payload, buffer.Length);
            }
            finally
            {
                fixed (NativeBuffer* p = &buffer)
                {
                    Table.BufRelease(p);
                }
            }
        }

        /// <summary>
        /// Reads a native buffer as bytes and releases it.
        /// </summary>
        internal static byte[] TakeBytes(ref NativeBuffer buffer)
        {
            try
            {
                if (buffer.Payload == IntPtr.Zero || buffer.Length <= 0)
                {
                    return Array.Empty<byte>();
                }

                var bytes = new byte[buffer.Length];
                fixed (byte* destination = bytes)
                {
                    Buffer.MemoryCopy((void*)buffer.Payload, destination, bytes.Length, bytes.Length);
                }

                return bytes;
            }
            finally
            {
                fixed (NativeBuffer* p = &buffer)
                {
                    Table.BufRelease(p);
                }
            }
        }

        /// <summary>
        /// The ht4c compatible environment defaults, as the native side reports them.
        /// </summary>
        internal static NativeConfig DefaultConfig()
        {
            NativeConfig config;
            Table.CfgDefaults(&config);
            return config;
        }
    }
}
