namespace Hyperlite.Persistence.Test.Serialization
{
    using System;
    using System.Runtime.Serialization;

    using Hyperlite.Persistence.Serialization;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Serialization callbacks, off by default: neither writing nor reading calls a type's
    ///     callback methods or IDeserializationCallback unless EnableSerializationCallbacks is on.
    /// </summary>
    [TestClass]
    public sealed class TestSerializationCallbacks
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            InvariantCulture.Pin();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            Encoder.Configuration.EnableSerializationCallbacks = false;
        }

        [TestMethod]
        public void CallbacksAreOffByDefault()
        {
            Assert.IsFalse(new EncoderConfiguration().EnableSerializationCallbacks);
            Assert.IsFalse(Encoder.Configuration.EnableSerializationCallbacks);
        }

        /// <summary>
        ///     Enabled, every callback runs, as it did before the setting.
        /// </summary>
        [TestMethod]
        public void EnabledEveryCallbackRuns()
        {
            Watched.Reset();
            Encoder.Configuration.EnableSerializationCallbacks = true;
            var copy = Deserializer.FromByteArray<Watched>(Serializer.ToByteArray(new Watched { Value = 5 }));

            Assert.AreEqual(5, copy.Value);
            Assert.AreEqual(1, Watched.Serializing);
            Assert.AreEqual(1, Watched.Serialized);
            Assert.AreEqual(1, Watched.Deserializing);
            Assert.AreEqual(1, Watched.Deserialized);
        }

        /// <summary>
        ///     By default reading calls none of them, and the value still reads.
        /// </summary>
        [TestMethod]
        public void ByDefaultReadingCallsNoCallback()
        {
            var bytes = Serializer.ToByteArray(new Watched { Value = 5 });
            Watched.Reset();

            var copy = Deserializer.FromByteArray<Watched>(bytes);

            Assert.AreEqual(5, copy.Value);
            Assert.AreEqual(0, Watched.Deserializing);
            Assert.AreEqual(0, Watched.Deserialized);
        }

        /// <summary>
        ///     By default writing calls none of them either.
        /// </summary>
        [TestMethod]
        public void ByDefaultWritingCallsNoCallback()
        {
            Watched.Reset();

            Serializer.ToByteArray(new Watched { Value = 5 });

            Assert.AreEqual(0, Watched.Serializing);
            Assert.AreEqual(0, Watched.Serialized);
        }

        /// <summary>
        ///     Enabled, an ISerializable type's callbacks run - IDeserializationCallback included.
        /// </summary>
        [TestMethod]
        public void EnabledASerializableTypesCallbacksRun()
        {
            var bytes = Serializer.ToByteArray(new WatchedSerializable { Value = 9 });
            WatchedSerializable.Reset();

            Encoder.Configuration.EnableSerializationCallbacks = true;

            var copy = Deserializer.FromByteArray<WatchedSerializable>(bytes);

            Assert.AreEqual(9, copy.Value);
            Assert.AreEqual(1, WatchedSerializable.Deserialized);
            Assert.AreEqual(1, WatchedSerializable.Deserialization);
        }

        /// <summary>
        ///     By default an ISerializable type's callbacks are left out as well; its
        ///     constructor, which is how it is read at all, still runs.
        /// </summary>
        [TestMethod]
        public void ByDefaultASerializableTypesCallbacksAreLeftOut()
        {
            var bytes = Serializer.ToByteArray(new WatchedSerializable { Value = 9 });
            WatchedSerializable.Reset();

            var copy = Deserializer.FromByteArray<WatchedSerializable>(bytes);

            Assert.AreEqual(9, copy.Value);
            Assert.AreEqual(0, WatchedSerializable.Deserialized);
            Assert.AreEqual(0, WatchedSerializable.Deserialization);
        }

        /// <summary>
        ///     Counts the callback methods the serializer calls. IDeserializationCallback is
        ///     only called for an ISerializable type, so it is counted on WatchedSerializable.
        /// </summary>
        public sealed class Watched
        {
            public static int Serializing { get; set; }

            public static int Serialized { get; set; }

            public static int Deserializing { get; set; }

            public static int Deserialized { get; set; }

            public int Value { get; set; }

            public static void Reset()
            {
                Serializing = Serialized = Deserializing = Deserialized = 0;
            }

            [OnSerializing]
            private void OnSerializing(StreamingContext context)
            {
                ++Serializing;
            }

            [OnSerialized]
            private void OnSerialized(StreamingContext context)
            {
                ++Serialized;
            }

            [OnDeserializing]
            private void OnDeserializing(StreamingContext context)
            {
                ++Deserializing;
            }

            [OnDeserialized]
            private void OnDeserialized(StreamingContext context)
            {
                ++Deserialized;
            }
        }

        [Serializable]
        public sealed class WatchedSerializable : ISerializable, IDeserializationCallback
        {
            public WatchedSerializable()
            {
            }

            private WatchedSerializable(SerializationInfo info, StreamingContext context)
            {
                this.Value = info.GetInt32("v");
            }

            public static int Deserialized { get; set; }

            public static int Deserialization { get; set; }

            public int Value { get; set; }

            public static void Reset()
            {
                Deserialized = Deserialization = 0;
            }

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.AddValue("v", this.Value);
            }

            public void OnDeserialization(object sender)
            {
                ++Deserialization;
            }

            [OnDeserialized]
            private void OnDeserialized(StreamingContext context)
            {
                ++Deserialized;
            }
        }
    }
}
