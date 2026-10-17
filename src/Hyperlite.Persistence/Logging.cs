namespace Hyperlite.Persistence
{
    using System;
    using System.Diagnostics;
#if DEBUG
    using System.Collections.Generic;
    using System.Linq;

#endif

    public static class Logging
    {
        private static readonly object SyncRoot = new object();

        /// <summary>
        ///     The trace source. Volatile, the enabled checks read it without taking the lock.
        /// </summary>
        private static volatile TraceSource traceSource;

        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="value" /> is null.
        /// </exception>
        public static TraceSource TraceSource
        {
            get
            {
                lock (SyncRoot)
                {
                    return GetTraceSource();
                }
            }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                lock (SyncRoot)
                {
                    traceSource = value;
                }
            }
        }

        /// <summary>
        ///     Checks if the specified trace event type is enabled.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if  the specified trace event type is enabled, otherwise <c>false</c>.
        /// </returns>
        public static bool IsEnabled(TraceEventType traceEventType)
        {
            // called on paths which trace nothing most of the time, so no lock here. The trace
            // source is volatile and its switch is only read.
            var source = traceSource;
            if (source == null)
            {
                lock (SyncRoot)
                {
                    source = GetTraceSource();
                }
            }

            if (source?.Switch != null)
            {
                try
                {
                    return source.Switch.ShouldTrace(traceEventType);
                }
                catch
                {
                }
            }

            return false;
        }

        /// <summary>
        ///     Writes a trace message to the trace source.
        /// </summary>
        /// <param name="message">
        ///     The trace message.
        /// </param>
        public static void TraceEvent(TraceEventType traceEventType, string message)
        {
            if (message != null && IsEnabled(traceEventType))
            {
                lock (SyncRoot)
                {
                    var source = GetTraceSource();
                    if (source?.Switch != null)
                    {
                        try
                        {
                            if (source.Switch.ShouldTrace(traceEventType))
                            {
                                source.TraceEvent(traceEventType, 0, message);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Writes a trace message to the trace source.
        /// </summary>
        /// <param name="func">
        ///     The trace message function.
        /// </param>
        public static void TraceEvent(TraceEventType traceEventType, Func<string> func)
        {
            if (func != null && IsEnabled(traceEventType))
            {
                lock (SyncRoot)
                {
                    var source = GetTraceSource();
                    if (source?.Switch != null)
                    {
                        try
                        {
                            if (source.Switch.ShouldTrace(traceEventType))
                            {
                                source.TraceEvent(traceEventType, 0, func());
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Writes a trace error message to the trace source.
        /// </summary>
        /// <param name="exception">
        ///     The exception to trace.
        /// </param>
        public static void TraceException(Exception exception)
        {
            if (exception != null)
            {
                lock (SyncRoot)
                {
                    var source = GetTraceSource();
                    if (source?.Switch != null)
                    {
                        try
                        {
                            if (source.Switch.ShouldTrace(TraceEventType.Error))
                            {
                                source.TraceEvent(TraceEventType.Error, 0, exception.ToString());
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private static TraceSource GetTraceSource()
        {
            if (traceSource == null)
            {
                traceSource = new TraceSource("Hyperlite.Persistence", SourceLevels.All);
#if DEBUG
                var listeners = new List<TraceListener>();
                listeners.AddRange(Trace.Listeners.OfType<TraceListener>());

                foreach (var listener in listeners)
                {
                    if (traceSource.Listeners.OfType<TraceListener>().All(l => l.Name != listener.Name))
                    {
                        traceSource.Listeners.Add(listener);
                    }
                }
#endif
            }

            return traceSource;
        }
    }
}
