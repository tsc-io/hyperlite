// Bytes allocated by this process, however the runtime it is on will tell us.
//
// Worth the indirection because allocation is the one column in the benchmark that is
// deterministic: it does not move when a neighbour process steals a core, so an
// allocation regression shows up in a single run where a timing regression needs several.
//
// The two runtimes expose it differently, and this is the only conditional compilation
// in the benchmark:
//
//   net8.0 and later  GC.GetTotalAllocatedBytes, which is exact and needs no opt in
//   net48             AppDomain.MonitoringTotalAllocatedMemorySize, which needs
//                     MonitoringIsEnabled set before anything is measured and cannot be
//                     turned off again

namespace Hyperlite.Bench
{
    using System;

    internal static class AllocationCounter
    {
        private static bool available;

        /// <summary>
        /// Whether a reading can be taken at all. False means the column reads zero
        /// rather than the run failing.
        /// </summary>
        internal static bool Available
        {
            get
            {
                return available;
            }
        }

        /// <summary>
        /// Turns the counter on. Call once, before the first measurement.
        /// </summary>
        internal static void Enable()
        {
#if NETFRAMEWORK
            try
            {
                if (!AppDomain.MonitoringIsEnabled)
                {
                    AppDomain.MonitoringIsEnabled = true;
                }

                available = true;
            }
            catch (Exception)
            {
                // hosted in something that forbids it; the column simply reads zero
                available = false;
            }
#else
            available = true;
#endif
        }

        /// <summary>
        /// Bytes allocated by this process since it started, or zero when unavailable.
        /// </summary>
        internal static double Read()
        {
            if (!available)
            {
                return 0.0;
            }

#if NETFRAMEWORK
            return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
#else
            return GC.GetTotalAllocatedBytes(true);
#endif
        }
    }
}
