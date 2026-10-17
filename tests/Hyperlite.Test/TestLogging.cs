namespace Hyperlite.Test
{
    using System;
    using System.Diagnostics;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     The trace surface, which measured 30% covered.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two things in here are contracts rather than implementation detail. The
    ///         trace source is named <c>Hyperlite</c>, and a consumer's
    ///         <c>system.diagnostics</c> configuration names it - if that name changed,
    ///         their listeners would silently stop receiving anything.
    ///         And <see cref="Logging.TraceEvent(TraceEventType, Func{string})" /> exists
    ///         only so that building a message costs nothing when the level is off; a
    ///         regression there is invisible except as a slowdown.
    ///     </para>
    ///     <para>
    ///         Every test here restores the switch level it found, because the source is
    ///         process-wide static state and the suites share a process.
    ///     </para>
    /// </remarks>
    [TestClass]
    public class TestLogging
    {
        /// <summary>
        ///     It was "ht4n" until the name was changed deliberately. Pinned either way,
        ///     because an app.config naming the source is how most callers configure
        ///     tracing and nothing at compile time connects the two: change this name and
        ///     their listeners go quiet with no error anywhere.
        /// </summary>
        [TestMethod]
        public void TraceSourceIsNamedAfterTheProduct()
        {
            var source = Logging.TraceSource;

            Assert.IsNotNull(source, "there is no trace source at all");
            Assert.AreEqual(
                "Hyperlite",
                source.Name,
                "the trace source name changed, so every consumer's system.diagnostics "
                + "configuration stops matching it and tracing goes silent");
        }

        /// <summary>
        ///     The lazy overload does not build its message when the level is off.
        /// </summary>
        /// <remarks>
        ///     This is the entire reason the overload exists. If it started evaluating the
        ///     delegate unconditionally, every trace call site would pay for a string it
        ///     throws away and nothing would fail.
        /// </remarks>
        [TestMethod]
        public void LazyMessageIsNotBuiltWhenDisabled()
        {
            var source = Logging.TraceSource;
            var restore = source.Switch.Level;

            try
            {
                source.Switch.Level = SourceLevels.Off;

                var built = 0;
                Logging.TraceEvent(TraceEventType.Verbose, () => { ++built; return "message"; });
                Assert.AreEqual(0, built, "the message was built although tracing is off");

                source.Switch.Level = SourceLevels.All;
                Logging.TraceEvent(TraceEventType.Verbose, () => { ++built; return "message"; });
                Assert.AreEqual(1, built, "the message was not built although tracing is on");
            }
            finally
            {
                source.Switch.Level = restore;
            }
        }

        [TestMethod]
        public void IsEnabledFollowsTheSwitch()
        {
            var source = Logging.TraceSource;
            var restore = source.Switch.Level;

            try
            {
                source.Switch.Level = SourceLevels.Off;
                Assert.IsFalse(Logging.IsEnabled(TraceEventType.Error));
                Assert.IsFalse(Logging.IsEnabled(TraceEventType.Verbose));

                source.Switch.Level = SourceLevels.Error;
                Assert.IsTrue(Logging.IsEnabled(TraceEventType.Error));
                Assert.IsFalse(Logging.IsEnabled(TraceEventType.Verbose));

                source.Switch.Level = SourceLevels.All;
                Assert.IsTrue(Logging.IsEnabled(TraceEventType.Error));
                Assert.IsTrue(Logging.IsEnabled(TraceEventType.Verbose));
            }
            finally
            {
                source.Switch.Level = restore;
            }
        }

        /// <summary>
        ///     Traced messages reach a listener on the source.
        /// </summary>
        /// <remarks>
        ///     A listener is the route that actually carries trace output - it is what an
        ///     app.config configuration installs, and the only way to observe what was
        ///     traced.
        /// </remarks>
        [TestMethod]
        public void TracedMessagesReachAListener()
        {
            var source = Logging.TraceSource;
            var restoreLevel = source.Switch.Level;
            var listener = new CollectingListener();

            try
            {
                source.Switch.Level = SourceLevels.All;
                source.Listeners.Add(listener);

                Logging.TraceEvent(TraceEventType.Error, "a message");

                Assert.AreEqual(1, listener.Messages.Count, "the listener received nothing");
                Assert.IsTrue(
                    listener.Messages[0].Contains("a message"),
                    "the listener received '" + listener.Messages[0] + "'");

                Logging.TraceEvent(TraceEventType.Error, () => "a lazy message");
                Assert.AreEqual(2, listener.Messages.Count);
                Assert.IsTrue(listener.Messages[1].Contains("a lazy message"));

                Logging.TraceException(new InvalidOperationException("an exception"));
                Assert.AreEqual(3, listener.Messages.Count, "TraceException traced nothing");
                Assert.IsTrue(
                    listener.Messages[2].Contains("an exception"),
                    "the exception message is not in '" + listener.Messages[2] + "'");
            }
            finally
            {
                source.Listeners.Remove(listener);
                source.Switch.Level = restoreLevel;
            }
        }

        /// <summary>
        ///     The two provider-facing properties round-trip and stay inert.
        /// </summary>
        /// <remarks>
        ///     <c>LogMessagePublished</c> is the sink for provider log messages and
        ///     <c>Logfile</c> names the provider's logfile. The SQLite provider emits
        ///     neither, so both are inert - but they are part of the surface an ht4n
        ///     consumer compiles against, so they have to keep existing and keep
        ///     round-tripping. Pinned rather than deleted, and pinned as inert so that
        ///     somebody finding them unused learns it is intended.
        /// </remarks>
        [TestMethod]
        public void ProviderFacingPropertiesRoundTripAndStayInert()
        {
            var restoreCallback = Logging.LogMessagePublished;
            var restoreLogfile = Logging.Logfile;
            var source = Logging.TraceSource;
            var restoreLevel = source.Switch.Level;

            try
            {
                Action<string> sink = _ => { };
                Logging.LogMessagePublished = sink;
                Assert.AreSame(sink, Logging.LogMessagePublished, "the sink does not round trip");

                Logging.Logfile = "some.log";
                Assert.AreEqual("some.log", Logging.Logfile, "the logfile does not round trip");

                Logging.LogMessagePublished = null;
                Assert.IsNull(Logging.LogMessagePublished);

                // and nothing the library traces reaches the sink: it is for provider
                // messages, and the SQLite provider emits none
                var called = 0;
                Logging.LogMessagePublished = _ => ++called;
                source.Switch.Level = SourceLevels.All;

                Logging.TraceEvent(TraceEventType.Error, "a message");
                Logging.TraceException(new InvalidOperationException("an exception"));

                Assert.AreEqual(
                    0,
                    called,
                    "the provider sink was invoked. Either a provider started emitting "
                    + "messages, which is news, or managed tracing was wired into it, which "
                    + "changes what a consumer's handler sees");
            }
            finally
            {
                source.Switch.Level = restoreLevel;
                Logging.LogMessagePublished = restoreCallback;
                Logging.Logfile = restoreLogfile;
            }
        }

        /// <summary>
        ///     Tracing never throws, whatever it is handed.
        /// </summary>
        /// <remarks>
        ///     Every call site here is on a failure path - something has already gone wrong
        ///     and is being reported. A trace call that throws replaces a diagnosable
        ///     failure with an undiagnosable one, so null arguments, a throwing message
        ///     builder and a throwing callback all have to be swallowed.
        /// </remarks>
        [TestMethod]
        public void TracingNeverThrows()
        {
            var source = Logging.TraceSource;
            var restoreLevel = source.Switch.Level;
            var restoreCallback = Logging.LogMessagePublished;

            try
            {
                source.Switch.Level = SourceLevels.All;

                Logging.TraceEvent(TraceEventType.Error, (string)null);
                Logging.TraceEvent(TraceEventType.Error, (Func<string>)null);
                Logging.TraceEvent(TraceEventType.Error, () => null);
                Logging.TraceException(null);
                Logging.TraceException(new InvalidOperationException("reported"));

                // a message builder that fails must not take the caller down with it
                Logging.TraceEvent(TraceEventType.Error, () => { throw new InvalidOperationException(); });

                // nor a callback that fails
                Logging.LogMessagePublished = _ => { throw new InvalidOperationException(); };
                Logging.TraceEvent(TraceEventType.Error, "a message");
            }
            finally
            {
                Logging.LogMessagePublished = restoreCallback;
                source.Switch.Level = restoreLevel;
            }
        }

        /// <summary>
        ///     Nothing reaches a listener when the level excludes it.
        /// </summary>
        [TestMethod]
        public void NothingIsTracedWhenFilteredOut()
        {
            var source = Logging.TraceSource;
            var restoreLevel = source.Switch.Level;
            var listener = new CollectingListener();

            try
            {
                source.Listeners.Add(listener);
                source.Switch.Level = SourceLevels.Error;

                Logging.TraceEvent(TraceEventType.Verbose, "filtered out");
                Assert.AreEqual(0, listener.Messages.Count, "a verbose message got through at level Error");

                Logging.TraceEvent(TraceEventType.Error, "let through");
                Assert.AreEqual(1, listener.Messages.Count, "an error message was dropped at level Error");

                source.Switch.Level = SourceLevels.Off;
                Logging.TraceEvent(TraceEventType.Error, "everything off");
                Assert.AreEqual(1, listener.Messages.Count, "a message got through at level Off");
            }
            finally
            {
                source.Listeners.Remove(listener);
                source.Switch.Level = restoreLevel;
            }
        }

        /// <summary>
        ///     A listener that keeps one entry per traced event.
        /// </summary>
        /// <remarks>
        ///     The TraceEvent overloads are what to override, not Write and WriteLine: a
        ///     source writes the header and the message separately, so collecting in those
        ///     records two entries for every event and every count is doubled.
        /// </remarks>
        private sealed class CollectingListener : TraceListener
        {
            internal System.Collections.Generic.List<string> Messages { get; } =
                new System.Collections.Generic.List<string>();

            public override void TraceEvent(
                TraceEventCache eventCache,
                string source,
                TraceEventType eventType,
                int id,
                string message)
            {
                this.Messages.Add(message ?? string.Empty);
            }

            public override void TraceEvent(
                TraceEventCache eventCache,
                string source,
                TraceEventType eventType,
                int id,
                string format,
                params object[] args)
            {
                this.Messages.Add(
                    args == null || format == null
                        ? format ?? string.Empty
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
            }

            public override void Write(string message)
            {
            }

            public override void WriteLine(string message)
            {
            }
        }
    }
}
