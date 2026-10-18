namespace Hyperlite.Persistence.Serialization
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    ///     Copies a block of bytes for the writers, the fastest way measured for its size.
    /// </summary>
    /// <remarks>
    ///     Below 64 KB the managed copy wins: a p/invoke costs more than the copy itself. From
    ///     64 KB up, on Windows, msvcrt's memcpy wins - a 1 MB array serialized 8% faster than
    ///     with the managed copy on net48, 4% on net10. Elsewhere the managed copy throughout,
    ///     msvcrt being Windows only; the check is at run time because one assembly serves both.
    /// </remarks>
    internal static unsafe class ByteCopy
    {
        private const int NativeThreshold = 64 * 1024;

        private static readonly bool Windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        internal static void Copy(byte* source, byte* destination, int count)
        {
            if (count >= NativeThreshold && Windows)
            {
                memcpy(destination, source, new UIntPtr((uint)count));
            }
            else
            {
                Buffer.MemoryCopy(source, destination, count, count);
            }
        }

        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        private static extern IntPtr memcpy(byte* destination, byte* source, UIntPtr count);
    }
}
