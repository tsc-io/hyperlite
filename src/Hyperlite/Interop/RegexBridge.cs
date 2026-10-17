namespace Hyperlite.Interop
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text.RegularExpressions;

#pragma warning disable CS3016 // an array in UnmanagedCallersOnly(CallConvs = ...) is reported even on these private, internal-class callbacks, which have no CLS surface
    /// <summary>
    /// Serves regex matching to Hyperlite.Native.
    /// </summary>
    /// <remarks>
    /// ht4c used RE2 for row, value and qualifier patterns. Hyperlite.Native carries no
    /// regex engine of its own, so matching is answered here instead.
    ///
    /// For the boolean question the scanner actually asks - does this pattern occur
    /// anywhere in this text - .NET Regex and RE2::PartialMatch agree on every pattern
    /// RE2 accepts, and .NET additionally accepts patterns RE2 rejects, which can only
    /// widen what works. The one place they could differ is a cell value that is not
    /// valid UTF-8, and <see cref="Utf8.ToStringLossless" /> handles that.
    ///
    /// The callbacks are only ever entered from the thread that called into the
    /// scanner, so no runtime thread attach is involved.
    /// </remarks>
    internal static unsafe class RegexBridge
    {
        /// <summary>
        /// Compiled patterns are long lived and few - one or two per scan - so
        /// RegexOptions.Compiled pays for itself over a scan of any size.
        /// </summary>
        private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

        /// <summary>
        /// How long one match may take. RE2 matched in linear time; .NET backtracks, and
        /// a pattern that backtracks catastrophically would otherwise run forever, holding
        /// the database. A process that sets REGEX_DEFAULT_MATCH_TIMEOUT decides instead.
        /// </summary>
        private static readonly TimeSpan MatchTimeout = GetMatchTimeout();

        /// <summary>
        /// Why the last pattern on this thread did not compile, see TakeCompileFailure.
        /// </summary>
        [ThreadStatic]
        private static Exception compileFailure;

#if NETFRAMEWORK || NETSTANDARD2_0
        private delegate IntPtr CompileCallback(byte* pattern, int patternLength);

        private delegate int MatchCallback(IntPtr handle, byte* text, int textLength);

        private delegate void ReleaseCallback(IntPtr handle);

        // the delegates have to outlive the native side, hence the static fields
        private static readonly CompileCallback CompileDelegate = Compile;

        private static readonly MatchCallback MatchDelegate = Match;

        private static readonly ReleaseCallback ReleaseDelegate = Release;
#endif

        /// <summary>
        /// Installs the callbacks. Called once, from <see cref="Native" />'s static
        /// constructor.
        /// </summary>
        internal static void Install()
        {
            NativeCallbacks callbacks;

#if NETFRAMEWORK || NETSTANDARD2_0
            callbacks.Compile = Marshal.GetFunctionPointerForDelegate(CompileDelegate);
            callbacks.Match = Marshal.GetFunctionPointerForDelegate(MatchDelegate);
            callbacks.Release = Marshal.GetFunctionPointerForDelegate(ReleaseDelegate);
#else
            callbacks.Compile = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int, IntPtr>)&Compile;
            callbacks.Match = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int>)&Match;
            callbacks.Release = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&Release;
#endif

            Native.Check(Native.Table.SetCallbacks(&callbacks));
        }

        /// <summary>
        /// Compiles a pattern. Returns <see cref="IntPtr.Zero" /> when the pattern does
        /// not compile, which the native side reports as BAD_SCAN_SPEC.
        /// </summary>
#if !NETFRAMEWORK && !NETSTANDARD2_0
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
#endif
        private static IntPtr Compile(byte* pattern, int patternLength)
        {
            // no exception may cross back into native code
            compileFailure = null;
            try
            {
                var text = Utf8.ToString(pattern, patternLength);
                if (text == null)
                {
                    return IntPtr.Zero;
                }

                var regex = new Regex(text, Options, MatchTimeout);
                return GCHandle.ToIntPtr(GCHandle.Alloc(regex));
            }
            catch (Exception e)
            {
                compileFailure = e;
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Hands over, once, why the last pattern on this thread did not compile. The native
        /// side reports only that it did not; the call that compiled it is still the one in
        /// progress when its failure is turned into an exception.
        /// </summary>
        internal static Exception TakeCompileFailure()
        {
            var failure = compileFailure;
            compileFailure = null;
            return failure;
        }

        /// <summary>
        /// Unanchored match, the equivalent of RE2::PartialMatch. Returns -1 when the match
        /// times out, which fails the scan with a <see cref="TimeoutException" />, and -2 when
        /// it fails otherwise, which fails the scan as well.
        /// </summary>
#if !NETFRAMEWORK && !NETSTANDARD2_0
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
#endif
        private static int Match(IntPtr handle, byte* text, int textLength)
        {
            try
            {
                if (handle == IntPtr.Zero)
                {
                    return 0;
                }

                var regex = (Regex)GCHandle.FromIntPtr(handle).Target;
                if (regex == null)
                {
                    return 0;
                }

                return regex.IsMatch(Utf8.ToStringLossless(text, textLength)) ? 1 : 0;
            }
            catch (RegexMatchTimeoutException)
            {
                // not no match: that would drop cells from the result without a word
                return -1;
            }
            catch (Exception e)
            {
                // nor here, for the same reason; and nothing may escape back into native code
                try
                {
                    Logging.TraceException(e);
                }
                catch (Exception)
                {
                }

                return -2;
            }
        }

        private static TimeSpan GetMatchTimeout()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData("REGEX_DEFAULT_MATCH_TIMEOUT") is TimeSpan timeout
                    && (timeout > TimeSpan.Zero || timeout == Regex.InfiniteMatchTimeout))
                {
                    return timeout;
                }
            }
            catch (Exception)
            {
            }

            return TimeSpan.FromSeconds(2);
        }

#if !NETFRAMEWORK && !NETSTANDARD2_0
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
#endif
        private static void Release(IntPtr handle)
        {
            try
            {
                if (handle != IntPtr.Zero)
                {
                    GCHandle.FromIntPtr(handle).Free();
                }
            }
            catch (Exception)
            {
            }
        }
    }
#pragma warning restore CS3016
}
