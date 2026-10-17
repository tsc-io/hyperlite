namespace Hyperlite.Test
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using System.Text;

    using Hyperlite;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Pins the native error code to exception type mapping.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Exception.cs</c> measured 7.7% covered, 373 lines untested - the largest
    ///         single gap in the library. What is in there is a contract: the comment on
    ///         <c>HyperliteExceptionFactory</c> says "the mapping is the one in ht4n's
    ///         HypertableException::Create ... Consumers catch these types by name, so
    ///         neither the names nor the codes may drift", and nothing asserted it.
    ///     </para>
    ///     <para>
    ///         An integrator writes <c>catch (TableNotFoundException)</c>. If code 22 ever
    ///         started producing something else, their handler would stop running and
    ///         nothing here would have noticed. That is what this fixture is for, and why
    ///         the table below is written out rather than derived from the factory - a test
    ///         generated from the code it tests cannot detect a change in it.
    ///     </para>
    ///     <para>
    ///         The factory is internal and there is no InternalsVisibleTo, so it is reached
    ///         by reflection. That is deliberate: adding InternalsVisibleTo to make a test
    ///         convenient would widen the assembly's surface, and the mapping is worth more
    ///         than the convenience.
    ///     </para>
    /// </remarks>
    [TestClass]
    public class TestExceptionMapping
    {
        /// <summary>
        ///     Every code the factory maps explicitly, and the type it has to produce.
        /// </summary>
        private static readonly Dictionary<int, Type> Expected = new Dictionary<int, Type>
            {
                { 1, typeof(ProtocolException) },
                { 2, typeof(RequestTruncatedException) },
                { 3, typeof(ResponseTruncatedException) },
                { 4, typeof(Hyperlite.TimeoutException) },
                { 5, typeof(IoException) },
                { 6, typeof(BadRootLocationException) },
                { 7, typeof(BadSchemaException) },
                { 8, typeof(InvalidMetadataException) },
                { 9, typeof(BadKeyException) },
                { 10, typeof(MetadataNotFoundException) },
                { 12, typeof(Hyperlite.FileNotFoundException) },
                { 22, typeof(TableNotFoundException) },
                { 23, typeof(MalformedRequestException) },
                { 24, typeof(TooManyColumnsException) },
                { 25, typeof(BadDomainNameException) },
                { 26, typeof(CommandParseException) },
                { 27, typeof(ConnectMasterException) },
                { 28, typeof(ConnectHyperspaceException) },
                { 29, typeof(BadMemoryAllocationException) },
                { 30, typeof(BadScanSpecException) },

                // the one code that maps to a framework type rather than a Hyperlite one
                { 31, typeof(NotImplementedException) },

                { 32, typeof(VersionMismatchException) },
                { 33, typeof(CancelledException) },
                { 34, typeof(SchemaParseException) },
                { 35, typeof(SyntaxException) },
                { 36, typeof(DoubleUngetException) },
                { 37, typeof(EmptyBloomFilterException) },
                { 38, typeof(BloomFilterChecksumMismatchException) },
                { 39, typeof(NameAlreadyInUseException) },
                { 40, typeof(NamespaceDoesNotExistException) },
                { 41, typeof(BadNamespaceException) },
                { 42, typeof(NamespaceExistsException) },
                { 43, typeof(NoResponseException) },
                { 44, typeof(NotAllowedException) },
                { 45, typeof(InducedFailureException) },
                { 46, typeof(ServerShuttingDownException) },
                { 47, typeof(LocationUnassignedException) },
                { 48, typeof(AlreadyExistsException) },
                { 49, typeof(ChecksumMismatchException) },
                { 50, typeof(ClosedException) },
                { 51, typeof(RangeServerNotFoundException) },
                { 52, typeof(ConnectionNotInitializedException) },

                // written in hex in the factory, and placed before the MasterException
                // range below - so this code is a TableExistsException and the Master
                // family effectively begins one later
                { 0x00040001, typeof(TableExistsException) }
            };

        /// <summary>
        ///     The families selected by a code range rather than a single value: inclusive
        ///     low, inclusive high, and the type.
        /// </summary>
        private static readonly object[][] Ranges =
            {
                new object[] { 1001, 1999, typeof(ConfigException) },
                new object[] { 0x00010001, 0x0001FFFF, typeof(CommException) },
                new object[] { 0x00020001, 0x0002FFFF, typeof(DfsBrokerException) },
                new object[] { 0x00030001, 0x0003FFFF, typeof(HyperspaceException) },
                // starts at 0x40002: 0x40001 is matched earlier as TableExistsException
                new object[] { 0x00040002, 0x0004FFFF, typeof(MasterException) },
                new object[] { 0x00050001, 0x0005FFFF, typeof(RangeServerException) },
                new object[] { 0x00070001, 0x0007FFFF, typeof(MetaLogException) },
                new object[] { 0x00080001, 0x0008FFFF, typeof(Hyperlite.SerializationException) },
                new object[] { 0x00090001, 0x0009FFFF, typeof(ThriftBrokerException) }
            };

        private static readonly MethodInfo Create = FindFactory();

        /// <summary>
        ///     Each explicitly mapped code produces exactly its type.
        /// </summary>
        [TestMethod]
        public void EveryMappedCodeProducesItsType()
        {
            var failures = new StringBuilder();

            foreach (var expected in Expected)
            {
                var actual = Build(expected.Key);
                if (actual.GetType() != expected.Value)
                {
                    failures.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "code {0}: expected {1}, got {2}",
                        expected.Key, expected.Value.Name, actual.GetType().Name));
                }
            }

            Assert.AreEqual(
                0,
                failures.Length,
                "the error code mapping changed, so a consumer's catch block no longer runs:"
                + Environment.NewLine + failures);

            Assert.AreEqual(43, Expected.Count, "the table itself was edited");
        }

        /// <summary>
        ///     Each range family covers its bounds and stops at them.
        /// </summary>
        /// <remarks>
        ///     The bounds are the whole point of a range: an off-by-one puts a
        ///     RangeServerException where a MasterException belongs, and only the first and
        ///     last code in each family can show it.
        /// </remarks>
        [TestMethod]
        public void RangeFamiliesCoverTheirBounds()
        {
            var failures = new StringBuilder();

            foreach (var range in Ranges)
            {
                var low = (int)range[0];
                var high = (int)range[1];
                var type = (Type)range[2];

                foreach (var code in new[] { low, low + 1, (int)(((long)low + high) / 2), high - 1, high })
                {
                    var actual = Build(code);
                    if (actual.GetType() != type)
                    {
                        failures.AppendLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "code 0x{0:x}: expected {1}, got {2}",
                            code, type.Name, actual.GetType().Name));
                    }
                }

                // one below the family is not in it - except for Master, whose
                // predecessor is the exact case 0x40001 rather than a gap
                if (low != 0x00040002)
                {
                    var below = Build(low - 1);
                    if (below.GetType() == type)
                    {
                        failures.AppendLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "code 0x{0:x} is one below {1} and produced it anyway",
                            low - 1, type.Name));
                    }
                }
            }

            Assert.AreEqual(0, failures.Length, Environment.NewLine + failures);
        }

        /// <summary>
        ///     A code in no family falls back to the base type rather than throwing or
        ///     returning null.
        /// </summary>
        [TestMethod]
        public void UnmappedCodesFallBackToTheBaseType()
        {
            foreach (var code in new[] { 0, -1, 11, 13, 21, 53, 999, 2000, 0x00060001, 0x000A0001, int.MaxValue })
            {
                var actual = Build(code);
                Assert.AreEqual(
                    typeof(HyperliteException),
                    actual.GetType(),
                    "code " + code + " should fall back to the base type, not a specific one");
            }
        }

        /// <summary>
        ///     The failure detail survives onto the exception.
        /// </summary>
        /// <remarks>
        ///     A mapping that produced the right type and lost the message, line or inner
        ///     exception would leave a caller with nothing to diagnose, and the type check
        ///     above would not notice.
        /// </remarks>
        [TestMethod]
        public void FailureDetailReachesTheException()
        {
            var inner = new InvalidOperationException("inner");
            var exception = (HyperliteException)Create.Invoke(
                null, new object[] { 22, "the message", 4711, "the func", "the file", inner });

            Assert.AreEqual(22, exception.ErrorCode);
            Assert.AreEqual("the message", exception.Message);
            Assert.AreEqual(4711, exception.ErrorLine);
            Assert.AreEqual("the func", exception.Func);
            Assert.AreEqual("the file", exception.File);
            Assert.AreSame(inner, exception.InnerException);

            // and a caller catching the base type catches the derived one
            try
            {
                throw exception;
            }
            catch (HyperliteException caught)
            {
                Assert.AreSame(exception, caught);
            }
        }

        /// <summary>
        ///     Null and empty detail is tolerated.
        /// </summary>
        /// <remarks>
        ///     The native side supplies these strings, and it supplies null for an unknown
        ///     function or file. Constructing the exception must not be the thing that
        ///     fails while reporting a failure.
        /// </remarks>
        [TestMethod]
        public void MissingDetailIsTolerated()
        {
            var exception = (HyperliteException)Create.Invoke(
                null, new object[] { 22, null, 0, null, null, null });

            Assert.IsNotNull(exception);
            Assert.AreEqual(22, exception.ErrorCode);
            Assert.IsNull(exception.InnerException);

            // normalised with ?? string.Empty rather than passed through, so a consumer
            // formatting these into a message never prints "null"
            Assert.AreEqual(string.Empty, exception.Func);
            Assert.AreEqual(string.Empty, exception.File);

            // Message is never null on an Exception; the framework substitutes a default
            Assert.IsNotNull(exception.Message);
        }

        /// <summary>
        ///     Every mapped type derives from the base, so one catch block can cover them.
        /// </summary>
        /// <remarks>
        ///     Except code 31, which is deliberately a framework NotImplementedException -
        ///     asserted explicitly so that the exception to the rule stays intentional.
        /// </remarks>
        [TestMethod]
        public void EveryMappedTypeDerivesFromTheBase()
        {
            foreach (var expected in Expected)
            {
                if (expected.Key == 31)
                {
                    Assert.AreEqual(typeof(NotImplementedException), expected.Value);
                    Assert.IsFalse(
                        typeof(HyperliteException).IsAssignableFrom(expected.Value),
                        "code 31 is meant to escape the Hyperlite hierarchy");
                    continue;
                }

                Assert.IsTrue(
                    typeof(HyperliteException).IsAssignableFrom(expected.Value),
                    expected.Value.Name + " does not derive from HyperliteException, so "
                    + "catch (HyperliteException) would miss it");
            }

            foreach (var range in Ranges)
            {
                Assert.IsTrue(
                    typeof(HyperliteException).IsAssignableFrom((Type)range[2]),
                    ((Type)range[2]).Name + " does not derive from HyperliteException");
            }
        }

        /// <summary>
        ///     The exception types are the ones an integrator names, so their names and
        ///     visibility are part of the contract.
        /// </summary>
        /// <remarks>
        ///     Pinned because a rename is a source break that compiles cleanly here: the
        ///     library would build, the tests would build, and a consumer's catch block
        ///     would stop compiling. One rename was authorised deliberately -
        ///     NamespaceDoesNotExistsException lost its middle s - and that is the kind of
        ///     change that should be a decision rather than a surprise.
        /// </remarks>
        [TestMethod]
        public void MappedTypesArePublicAndNamedAsDocumented()
        {
            var failures = new StringBuilder();

            foreach (var type in Expected.Values)
            {
                if (type == typeof(NotImplementedException))
                {
                    continue;
                }

                if (!type.IsPublic)
                {
                    failures.AppendLine(type.Name + " is not public, so it cannot be caught by name");
                }

                if (type.Assembly != typeof(HyperliteException).Assembly)
                {
                    failures.AppendLine(type.Name + " is not in the Hyperlite assembly");
                }
            }

            Assert.AreEqual(0, failures.Length, Environment.NewLine + failures);

            // the authorised rename, spelled the new way
            Assert.IsNotNull(
                typeof(HyperliteException).Assembly.GetType("Hyperlite.NamespaceDoesNotExistException"),
                "NamespaceDoesNotExistException is gone or renamed again");
            Assert.IsNull(
                typeof(HyperliteException).Assembly.GetType("Hyperlite.NamespaceDoesNotExistsException"),
                "the old misspelling is back");
        }

        /// <summary>
        ///     The failure detail survives GetObjectData, which is how these exceptions
        ///     cross an app domain or a serializer.
        /// </summary>
        /// <remarks>
        ///     ht4n's exceptions were binary serializable and the surface was kept for that
        ///     reason, so the override has to carry the four fields that make the exception
        ///     worth having. Asserted through SerializationInfo directly rather than through
        ///     BinaryFormatter, which is removed on modern .NET - the override is the
        ///     contract, not the formatter that used to call it.
        /// </remarks>
        [TestMethod]
        public void DetailSurvivesGetObjectData()
        {
            var exception = (HyperliteException)Create.Invoke(
                null, new object[] { 22, "the message", 4711, "the func", "the file", null });

            var info = new System.Runtime.Serialization.SerializationInfo(
                exception.GetType(), new System.Runtime.Serialization.FormatterConverter());

            exception.GetObjectData(info, default(System.Runtime.Serialization.StreamingContext));

            var written = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (System.Runtime.Serialization.SerializationEntry entry in info)
            {
                written[entry.Name] = entry.Value;
            }

            var failures = new StringBuilder();
            foreach (var expected in new Dictionary<string, object>
                {
                    // the names GetObjectData writes, which are the field names rather
                    // than the property names - part of the serialized form, so pinned
                    { "code", 22 },
                    { "line", 4711 },
                    { "func", "the func" },
                    { "file", "the file" }
                })
            {
                object actual;
                if (!written.TryGetValue(expected.Key, out actual))
                {
                    failures.AppendLine(expected.Key + " was not written");
                }
                else if (!Equals(expected.Value, actual))
                {
                    failures.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: expected {1}, got {2}", expected.Key, expected.Value, actual));
                }
            }

            Assert.AreEqual(
                0,
                failures.Length,
                "the exception no longer carries its detail through serialization:"
                + Environment.NewLine + failures);
        }

        private static Exception Build(int errorCode)
        {
            var exception = (Exception)Create.Invoke(
                null,
                new object[] { errorCode, "message", 1, "func", "file", null });

            Assert.IsNotNull(exception, "the factory returned null for code " + errorCode);
            return exception;
        }

        private static MethodInfo FindFactory()
        {
            var factory = typeof(HyperliteException).Assembly.GetType(
                "Hyperlite.HyperliteExceptionFactory");

            Assert.IsNotNull(
                factory,
                "HyperliteExceptionFactory is gone or renamed; this fixture reaches it by "
                + "reflection because it is internal");

            var create = factory.GetMethod(
                "Create", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.IsNotNull(create, "HyperliteExceptionFactory.Create is gone or its signature changed");
            return create;
        }
    }
}
