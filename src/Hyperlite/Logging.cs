namespace Hyperlite
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Diagnostic tracing for Hyperlite.
    /// </summary>
    /// <remarks>
    /// Everything Hyperlite traces goes through the "Hyperlite"
    /// <see cref="System.Diagnostics.TraceSource" />. It was called "ht4n" until the name
    /// was deliberately changed: a consumer carrying a system.diagnostics configuration
    /// over from ht4n has to rename the source there as well, or their listeners receive
    /// nothing.
    ///
    /// <see cref="Logfile" /> and <see cref="LogMessagePublished" /> exist for
    /// compatibility. In ht4n they surfaced log4cpp output from the Hypertable client
    /// libraries; the SQLite provider has no such logging, so there is nothing to
    /// forward and no logfile to write. Setting them is accepted and remembered rather
    /// than rejected, so a caller carried over from ht4n does not have to change.
    /// </remarks>
    public static class Logging
    {
        private static readonly object SyncRoot = new object();

        private static TraceSource traceSource;

        private static Action<string> logMessagePublished;

        private static string logfile = string.Empty;

        /// <summary>
        /// Gets or sets the trace source Hyperlite writes to.
        /// </summary>
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
        /// Gets or sets the sink for provider log messages.
        /// </summary>
        /// <remarks>
        /// The SQLite provider emits none, so a handler installed here is never called.
        /// </remarks>
        public static Action<string> LogMessagePublished
        {
            get
            {
                lock (SyncRoot)
                {
                    return logMessagePublished;
                }
            }

            set
            {
                lock (SyncRoot)
                {
                    logMessagePublished = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the provider logfile.
        /// </summary>
        /// <remarks>
        /// The SQLite provider writes no logfile; the value is remembered so a round trip
        /// through this property behaves, but nothing reads it.
        /// </remarks>
        public static string Logfile
        {
            get
            {
                lock (SyncRoot)
                {
                    return logfile;
                }
            }

            set
            {
                lock (SyncRoot)
                {
                    logfile = value ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Determines whether the given event type would be traced.
        /// </summary>
        /// <returns>true if tracing is enabled for that type, otherwise false.</returns>
        public static bool IsEnabled(TraceEventType traceEventType)
        {
            lock (SyncRoot)
            {
                var source = GetTraceSource();
                if (source != null && source.Switch != null)
                {
                    try
                    {
                        return source.Switch.ShouldTrace(traceEventType);
                    }
                    catch (Exception)
                    {
                        // a misconfigured switch must not take the caller down, which is
                        // how ht4n treated it too
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Traces a message.
        /// </summary>
        /// <param name="traceEventType">Event type.</param>
        /// <param name="message">Message, ignored when null.</param>
        public static void TraceEvent(TraceEventType traceEventType, string message)
        {
            if (message == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                Write(traceEventType, message);
            }
        }

        /// <summary>
        /// Traces a message, evaluating it only when it would actually be written.
        /// </summary>
        /// <param name="traceEventType">Event type.</param>
        /// <param name="func">Message factory, ignored when null.</param>
        public static void TraceEvent(TraceEventType traceEventType, Func<string> func)
        {
            if (func == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                var source = GetTraceSource();
                if (source == null || source.Switch == null)
                {
                    return;
                }

                try
                {
                    if (source.Switch.ShouldTrace(traceEventType))
                    {
                        source.TraceEvent(traceEventType, 0, func());
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// Traces an exception as an error.
        /// </summary>
        /// <param name="exception">Exception, ignored when null.</param>
        public static void TraceException(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                Write(TraceEventType.Error, exception.ToString());
            }
        }

        private static void Write(TraceEventType traceEventType, string message)
        {
            var source = GetTraceSource();
            if (source == null || source.Switch == null)
            {
                return;
            }

            try
            {
                if (source.Switch.ShouldTrace(traceEventType))
                {
                    source.TraceEvent(traceEventType, 0, message);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// The trace source, created on first use. Called under the lock.
        /// </summary>
        private static TraceSource GetTraceSource()
        {
            if (traceSource == null)
            {
                traceSource = new TraceSource("Hyperlite", SourceLevels.All);
            }

            return traceSource;
        }
    }
}
