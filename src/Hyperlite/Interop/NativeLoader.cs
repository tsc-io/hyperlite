namespace Hyperlite.Interop
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Locates and loads Hyperlite.Native.
    /// </summary>
    /// <remarks>
    /// DllImport is deliberately not used. The library name carries the runtime
    /// identifier and its single entry point is exported by ordinal only on Windows,
    /// neither of which DllImport can express, and net48 has no
    /// NativeLibrary.SetDllImportResolver to work around it. Loading by hand covers
    /// every target framework with one code path.
    /// </remarks>
    internal static class NativeLoader
    {
        /// <summary>
        /// The entry point name. Only used where ordinals do not exist, which is
        /// everywhere except Windows.
        /// </summary>
        private const string EntryPointName = "q4kf9wz";

        /// <summary>
        /// The ordinal the Windows build exports the entry point under, see
        /// Hyperlite.Native.def.
        /// </summary>
        private const int EntryPointOrdinal = 1;

        /// <summary>
        /// Loads the library and returns the address of its entry point.
        /// </summary>
        /// <returns>Entry point address, never <see cref="IntPtr.Zero" />.</returns>
        /// <exception cref="DllNotFoundException">The library could not be found or loaded.</exception>
        /// <exception cref="EntryPointNotFoundException">The library has no usable entry point.</exception>
        internal static IntPtr LoadEntryPoint()
        {
            var attempted = new List<string>();
            var handle = IntPtr.Zero;
            string loaded = null;

            foreach (var candidate in Candidates())
            {
                attempted.Add(candidate);
                handle = Load(candidate);
                if (handle != IntPtr.Zero)
                {
                    loaded = candidate;
                    break;
                }
            }

            if (handle == IntPtr.Zero)
            {
                var message = new StringBuilder();
                message.Append("Unable to load ").Append(LibraryFileName()).Append('.');
                message.Append(" Probed: ");
                message.Append(string.Join(", ", attempted.ToArray()));
                throw new DllNotFoundException(message.ToString());
            }

            var entry = GetEntryPoint(handle);
            if (entry == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException(
                    "Loaded " + loaded + " but it does not export the expected entry point. " +
                    "The library is not a matching Hyperlite.Native build.");
            }

            return entry;
        }

        /// <summary>
        /// Gets the runtime identifier the native companion is named after.
        /// </summary>
        internal static string RuntimeIdentifier()
        {
            string os;
#if NETFRAMEWORK
            os = "win";
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                os = "win";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                os = "linux";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                os = "osx";
            }
            else
            {
                os = "unknown";
            }
#endif

            string architecture;
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X64:
                    architecture = "x64";
                    break;
                case Architecture.X86:
                    architecture = "x86";
                    break;
                case Architecture.Arm64:
                    architecture = "arm64";
                    break;
                case Architecture.Arm:
                    architecture = "arm";
                    break;
                default:
                    architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
                    break;
            }

            return os + "-" + architecture;
        }

        /// <summary>
        /// Gets the platform specific file name of the native companion.
        /// </summary>
        private static string LibraryFileName()
        {
            var rid = RuntimeIdentifier();
#if NETFRAMEWORK
            return "Hyperlite.Native." + rid + ".dll";
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "Hyperlite.Native." + rid + ".dll";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "libHyperlite.Native." + rid + ".dylib";
            }

            return "libHyperlite.Native." + rid + ".so";
#endif
        }

        /// <summary>
        /// Yields the paths to try, most specific first. The last entry is the bare
        /// file name, which lets the operating system apply its own search rules.
        /// </summary>
        private static IEnumerable<string> Candidates()
        {
            var fileName = LibraryFileName();
            var rid = RuntimeIdentifier();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in Roots())
            {
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                // the nuget layout first, then flat beside the managed assembly
                var candidates = new[]
                {
                    Path.Combine(Path.Combine(Path.Combine(root, "runtimes"), rid), Path.Combine("native", fileName)),
                    Path.Combine(root, fileName)
                };

                foreach (var candidate in candidates)
                {
                    if (seen.Add(candidate) && File.Exists(candidate))
                    {
                        yield return candidate;
                    }
                }
            }

            yield return fileName;
        }

        /// <summary>
        /// Yields the directories to probe: where the process was started, and where
        /// this assembly actually sits. They differ when Hyperlite is loaded from a
        /// package cache or a plugin directory.
        /// </summary>
        private static IEnumerable<string> Roots()
        {
            string baseDirectory = null;
            try
            {
                baseDirectory = AppContext.BaseDirectory;
            }
            catch (Exception)
            {
                // a host without a base directory is unusual but not fatal, the
                // assembly location below still applies
            }

            if (!string.IsNullOrEmpty(baseDirectory))
            {
                yield return baseDirectory;
            }

            string assemblyDirectory = null;
            try
            {
                var location = typeof(NativeLoader).Assembly.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    assemblyDirectory = Path.GetDirectoryName(location);
                }
            }
            catch (Exception)
            {
                // single file and some trimmed hosts report no location
            }

            if (!string.IsNullOrEmpty(assemblyDirectory))
            {
                yield return assemblyDirectory;
            }
        }

        private static IntPtr Load(string path)
        {
#if NETFRAMEWORK
            return Windows.LoadLibraryEx(path, IntPtr.Zero, Windows.LoadWithAlteredSearchPath);
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Windows.LoadLibraryEx(path, IntPtr.Zero, Windows.LoadWithAlteredSearchPath);
            }

#if NETCOREAPP
            return NativeLibrary.TryLoad(path, out var handle) ? handle : IntPtr.Zero;
#else
            return Unix.Open(path);
#endif
#endif
        }

        private static IntPtr GetEntryPoint(IntPtr handle)
        {
#if NETFRAMEWORK
            return Windows.GetProcAddress(handle, new IntPtr(EntryPointOrdinal));
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // exported NONAME, so the ordinal is the only way in
                return Windows.GetProcAddress(handle, new IntPtr(EntryPointOrdinal));
            }

#if NETCOREAPP
            return NativeLibrary.TryGetExport(handle, EntryPointName, out var entry) ? entry : IntPtr.Zero;
#else
            return Unix.Symbol(handle, EntryPointName);
#endif
#endif
        }

        private static class Windows
        {
            internal const uint LoadWithAlteredSearchPath = 0x00000008;

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, BestFitMapping = false)]
            internal static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);

            /// <summary>
            /// Resolves an export by ordinal. The name overload is deliberately absent,
            /// the library exports nothing by name.
            /// </summary>
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);
        }

#if !NETFRAMEWORK && !NETCOREAPP
        /// <summary>
        /// dlopen for netstandard2.0, which has no NativeLibrary. Since glibc 2.34 it lives in
        /// libc.so.6 and libdl.so.2 is only a stub that may not be installed, older glibc has it
        /// in libdl.so.2 only.
        /// </summary>
        private static class Unix
        {
            private const int RtldNow = 2;

            private const int RtldLocal = 0;

            private static bool useLibc;

            internal static IntPtr Open(string fileName)
            {
                if (!useLibc)
                {
                    try
                    {
                        return Dl.dlopen(fileName, RtldNow | RtldLocal);
                    }
                    catch (DllNotFoundException)
                    {
                        useLibc = true;
                    }
                }

                return Libc.dlopen(fileName, RtldNow | RtldLocal);
            }

            internal static IntPtr Symbol(IntPtr handle, string symbol)
            {
                return useLibc ? Libc.dlsym(handle, symbol) : Dl.dlsym(handle, symbol);
            }

            private static class Dl
            {
                [DllImport("libdl.so.2", CharSet = CharSet.Ansi, BestFitMapping = false)]
                internal static extern IntPtr dlopen(string fileName, int flags);

                [DllImport("libdl.so.2", CharSet = CharSet.Ansi, BestFitMapping = false)]
                internal static extern IntPtr dlsym(IntPtr handle, string symbol);
            }

            private static class Libc
            {
                [DllImport("libc.so.6", CharSet = CharSet.Ansi, BestFitMapping = false)]
                internal static extern IntPtr dlopen(string fileName, int flags);

                [DllImport("libc.so.6", CharSet = CharSet.Ansi, BestFitMapping = false)]
                internal static extern IntPtr dlsym(IntPtr handle, string symbol);
            }
        }
#endif
    }
}
