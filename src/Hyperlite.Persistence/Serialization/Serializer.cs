namespace Hyperlite.Persistence.Serialization
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using Hyperlite.Persistence.Collections;
    using Hyperlite.Persistence.Collections.Concurrent;
    using Hyperlite.Persistence.Extensions;
    using Hyperlite.Persistence.Reflection;
    using Hyperlite.Persistence.Serialization.Delegates;

    public class Serializer : SerializationBase
    {
        private static readonly ConcurrentTypeDictionary<TypeSchema> TypeSchemaDictionary =
            new ConcurrentTypeDictionary<TypeSchema>();

        private static readonly Type LinqEnumerableType = typeof(System.Linq.Enumerable);

        // The two encoders that need the serializer's configuration, shared across all
        // serializers.
        //
        // They used to be built in the constructor as bound instance methods, which cost
        // two delegates and two EncoderInfo objects - 256 bytes - on every serializer, and
        // a serializer is constructed per serialized value. They take the serializer as an
        // argument now, so one instance of each serves everybody and the configuration is
        // still read per call, from whichever serializer is writing.

        /// <summary>
        ///     The encoder for Type, which the configuration's type writer and strict type
        ///     codes apply to.
        /// </summary>
        private static readonly EncoderInfo TypeEncoderInfo =
            new EncoderInfo(Tags.Type, (EncodeWithSerializer)WriteTypeOf);

        /// <summary>
        ///     The encoder for DateTime, which the configuration's date time behavior
        ///     applies to.
        /// </summary>
        private static readonly EncoderInfo DateTimeEncoderInfo =
            new EncoderInfo(Tags.DateTime, (EncodeWithSerializer)WriteDateTimeOf);

        /// <summary>
        ///     Initial capacity of the per serialization scratch dictionaries.
        /// </summary>
        /// <remarks>
        ///     A serializer instance lives for one entity, nested entities get their own. These
        ///     dictionaries were created with a capacity of 256 each, which allocated and cleared
        ///     about 30kB per entity while a typical entity graph holds a handful of objects, types
        ///     and strings. They grow on demand, so a larger graph only pays for what it uses.
        ///     <para>
        ///     28 rather than a round number because the dictionaries are flat tables,
        ///     which hold seven eighths of their slots: 28 is exactly 32 slots, where 32 would
        ///     be 64.
        ///     </para>
        /// </remarks>
        private const int ScratchCapacity = 28;

        /// <summary>
        ///     Initial capacity of the per serialization encoder cache.
        /// </summary>
        /// <remarks>
        ///     Unlike the back reference dictionaries this one is always allocated, because
        ///     the first value written consults it - so its size is paid on every serialize,
        ///     and at 32 that was 792 of the 1,388 bytes a four byte int cost.
        ///     <para>
        ///     What it has to hold is not the size of the graph but the number of distinct
        ///     types in it, and that stays small: an entity is written through its type
        ///     schema, so its property types never reach this cache. Measured, a flat entity
        ///     with 18 distinct property types occupies no more entries than a bare int does.
        ///     Only structural generics consult it per element type.
        ///     </para>
        ///     <para>
        ///     So the size is a wager on that count, and 16 wins it: nothing in the benchmark
        ///     suite reaches ten distinct types, and the worst case there is a nested generic
        ///     of fourteen. 8 was measured too and already grows on a three element tuple,
        ///     which costs time as well as bytes.
        ///     </para>
        ///     <para>
        ///     14 rather than 16 because the cache is a flat table, which holds seven
        ///     eighths of its slots: 14 is exactly 16 slots, where 16 would be 32. It is
        ///     still above the ten distinct types nothing in the suite reaches.
        ///     </para>
        /// </remarks>
        private const int EncoderCapacity = 14;

        private EncoderConfiguration configuration;

        private readonly FlatDictionary<Type, EncoderInfo, TypeComparer> encoderInfos =
            new FlatDictionary<Type, EncoderInfo, TypeComparer>(EncoderCapacity);

        // The four back-reference dictionaries, allocated on first use.
        //
        // A Serializer is constructed per ToByteArray call, and these cost about 600
        // bytes each at ScratchCapacity - which is why serializing a four byte int used
        // to allocate 4,557 bytes. A scalar refers to nothing, so it now allocates none
        // of them; an object graph allocates exactly what it touches, when it touches it.
        //
        // The format is unaffected: an absent dictionary and an empty one write the same
        // bytes, because both mean "nothing has been referenced yet".

        /// <summary>
        ///     The identity dictionary, or null until an object reference is written.
        /// </summary>
        private IdentityDictionary<int> identityDictionary;

        /// <summary>
        ///     The string dictionary, or null until a string reference is written.
        /// </summary>
        /// <remarks>
        ///     Keyed on the member names an ISerializable type writes and on the column
        ///     family and qualifier of every entity reference - chosen at run time, and a
        ///     qualifier is often data - so it must not be floodable. On .NET 8 and later the
        ///     runtime's string hash is randomized per process and the flat table is safe. On
        ///     .NET Framework it is not randomized and stops at the first NUL character, and
        ///     the flat table has no defence against keys that collide; the framework's Dictionary
        ///     switches itself to a randomized comparer once a chain grows past 100.
        ///     netstandard2.0 as well: a net461 to net472 application resolves to that build.
        ///     TestHashFlooding pins it.
        /// </remarks>
#if NET8_0_OR_GREATER
        private FlatDictionary<string, int, Collections.StringComparer> stringDictionary;
#else
        private Dictionary<string, int> stringDictionary;
#endif

        /// <summary>
        ///     The type dictionary, or null until a type reference is written.
        /// </summary>
        private FlatDictionary<Type, int, TypeComparer> typeDictionary;

        /// <summary>
        ///     The type schema reference dictionary, or null until a schema is written.
        /// </summary>
        private FlatDictionary<Type, TypeSchemaRef, TypeComparer> typeSchemaRefDictionary;

        private BinaryWriter binaryWriter;

        private int identityValue;

        private StreamingContext streamingContext;

        public Serializer()
            : this(null, null, null)
        {
        }

        public Serializer(EncoderConfiguration configuration)
            : this(null, configuration, null)
        {
        }

        protected Serializer(BinaryWriter binaryWriter)
            : this(binaryWriter, null, null)
        {
        }

        protected Serializer(BinaryWriter binaryWriter, EncoderConfiguration configuration, object streamingContext)
        {
            this.binaryWriter = binaryWriter;
            this.configuration = EncoderConfiguration.CreateFrom(configuration);
            this.streamingContext = new StreamingContext(StreamingContextStates.Persistence, streamingContext);

            this.encoderInfos.Add(typeof(Type).GetType(), TypeEncoderInfo);
            this.encoderInfos.Add(typeof(DateTime), DateTimeEncoderInfo);
        }

        /// <summary>
        ///     Gets the identity dictionary, creating it on first use.
        /// </summary>
        private IdentityDictionary<int> IdentityRefs
        {
            get
            {
                return this.identityDictionary
                       ?? (this.identityDictionary = new IdentityDictionary<int>(ScratchCapacity));
            }
        }

        /// <summary>
        ///     Gets the string dictionary, creating it on first use.
        /// </summary>
#if NET8_0_OR_GREATER
        private FlatDictionary<string, int, Collections.StringComparer> StringRefs
        {
            get
            {
                return this.stringDictionary
                       ?? (this.stringDictionary = new FlatDictionary<string, int, Collections.StringComparer>(ScratchCapacity));
            }
        }
#else
        private Dictionary<string, int> StringRefs
        {
            get
            {
                // no comparer: only the default one is replaced by a randomized comparer
                // when a chain grows too long
                return this.stringDictionary
                       ?? (this.stringDictionary = new Dictionary<string, int>(ScratchCapacity));
            }
        }
#endif

        /// <summary>
        ///     Gets the type dictionary, creating it on first use.
        /// </summary>
        private FlatDictionary<Type, int, TypeComparer> TypeRefs
        {
            get
            {
                return this.typeDictionary
                       ?? (this.typeDictionary = new FlatDictionary<Type, int, TypeComparer>(ScratchCapacity));
            }
        }

        /// <summary>
        ///     Gets the type schema reference dictionary, creating it on first use.
        /// </summary>
        private FlatDictionary<Type, TypeSchemaRef, TypeComparer> TypeSchemaRefs
        {
            get
            {
                return this.typeSchemaRefDictionary
                       ?? (this.typeSchemaRefDictionary =
                               new FlatDictionary<Type, TypeSchemaRef, TypeComparer>(ScratchCapacity));
            }
        }

        public BinaryWriter BinaryWriter => this.binaryWriter;

        public EncoderConfiguration Configuration
        {
            get
            {
                // Asking for the configuration may well mean intending to change it, and a
                // change must not reach every other serializer - so the shared default
                // snapshot is copied on the way out, and this serializer keeps the copy.
                //
                // That works only because the two configuration-sensitive encoders read
                // this field when they run rather than capturing it at construction: the
                // copy takes effect for the rest of this serialization.
                if (this.configuration.IsDefaultSnapshot)
                {
                    this.configuration = new EncoderConfiguration(this.configuration);
                }

                return this.configuration;
            }
        }

        /// <summary>
        ///     Serializes the object specified.
        /// </summary>
        /// <param name="value">
        ///     The object to serialize.
        /// </param>
        /// <typeparam name="T">
        ///     Type of the object to serialize.
        /// </typeparam>
        /// <returns>
        ///     The serialized object.
        /// </returns>
        public static byte[] ToByteArray<T>(T value)
        {
            return ToByteArray(value, SerializationBase.DefaultCapacity);
        }

        /// <summary>
        ///     Serializes the object specified.
        /// </summary>
        /// <param name="value">
        ///     The object to serialize.
        /// </param>
        /// <param name="capacity">
        ///     The internal memory stream initial capacity.
        /// </param>
        /// <typeparam name="T">
        ///     Type of the object to serialize.
        /// </typeparam>
        /// <returns>
        ///     The serialized object.
        /// </returns>
        public static byte[] ToByteArray<T>(T value, int capacity)
        {
            return ToByteArray(typeof(T), value, capacity);
        }

        /// <summary>
        ///     Serializes the object specified.
        /// </summary>
        /// <param name="value">
        ///     The object to serialize.
        /// </param>
        /// <param name="capacity">
        ///     The internal memory stream initial capacity.
        /// </param>
        /// <returns>
        ///     The serialized object.
        /// </returns>
        public static byte[] ToByteArray(Type serializeType, object value, int capacity)
        {
            using (var binaryWriter = new HeapBinaryWriter(capacity))
            {
                new Serializer(binaryWriter).Write(serializeType, value);
                return binaryWriter.ToArray();
            }
        }

        /// <summary>
        ///     Deserialize from the stream specified.
        /// </summary>
        /// <typeparam name="T">
        ///     Type of the object to deserialize.
        /// </typeparam>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        public T Deserialize<T>(Stream stream)
        {
            return Deserializer.Deserialize<T>(stream);
        }

        /// <summary>
        ///     Deserialize from the stream specified.
        /// </summary>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        public object Deserialize(Type destinationType, Stream stream)
        {
            return Deserializer.Deserialize(destinationType, stream);
        }

        /// <summary>
        ///     Serializes the object specified.
        /// </summary>
        /// <typeparam name="T">
        ///     Type of the object to serialize.
        /// </typeparam>
        /// <param name="value">
        ///     The object to serialize.
        /// </param>
        public void Serialize<T>(Stream stream, T value)
        {
            this.Serialize(stream, typeof(T), value);
        }

        /// <summary>
        ///     Serializes the object specified.
        /// </summary>
        /// <typeparam name="T">
        ///     Type of the object to serialize.
        /// </typeparam>
        /// <param name="value">
        ///     The object to serialize.
        /// </param>
        public void Serialize<T>(BinaryWriter binaryWriter, T value)
        {
            this.Serialize(binaryWriter, typeof(T), value);
        }

        /// <param name="value">
        ///     The object to serialize.
        /// </param>
        public void Serialize(Stream stream, Type serializeType, object value)
        {
            using (this.binaryWriter = new BufferedBinaryWriter(stream, true))
            {
                this.Write(serializeType, value);
            }
        }

        /// <param name="value">
        ///     The object to serialize.
        /// </param>
        public void Serialize(BinaryWriter binaryWriter, Type serializeType, object value)
        {
            this.binaryWriter = binaryWriter;
            this.Write(serializeType, value);
        }

        /// <param name="value">
        ///     The object to write.
        /// </param>
        public void WriteObject(object value)
        {
            this.Write(value?.GetType() ?? typeof(object), value);
        }

        /// <summary>
        ///     Determine if the type tag requires a collection element tag.
        /// </summary>
        /// <param name="tag">
        ///     The type tag.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the type requires a collection element tag, otherwise <c>false</c>.
        /// </returns>
        internal static bool HasElementTag(Tags tag, Type type)
        {
            tag &= ~Tags.Array;
            return tag == Tags.Object || tag == Tags.String || tag == Tags.Tuple || tag == Tags.Uri
                   || (tag >= Tags.FirstCustomType && type.IsClass);
        }

        /// <param name="value">
        ///     The object to encode.
        /// </param>
        /// <param name="writeTag">
        ///     If <c>true</c> the encoder writes the leading type tag, otherwise <c>false</c>.
        /// </param>
        internal virtual void Encode(EncoderInfo encoderInfo, object value, bool writeTag)
        {
            if (value != null && encoderInfo.HandleObjectRef(value.GetType()))
            {
                if (this.WriteOrAddObjectRef(value))
                {
                    return;
                }
            }

            encoderInfo.Encode(this, value, writeTag);
        }

        internal virtual TypeSchema GetTypeSchema(Type type, Inspector inspector)
        {
            return TypeSchemaDictionary.GetOrAdd(type, inspector, (t, i) => new TypeSchema(i, false));
        }

        /// <param name="type">
        ///     The object type.
        /// </param>
        /// <param name="value">
        ///     The object to write.
        /// </param>
        internal void Write(Type serializeType, Type type, object value)
        {
            // every nested value comes through here: an overflowed stack ends the process,
            // this throws
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException e)
            {
                throw new SerializationException(@"The value is nested too deeply to write", e);
            }

            var inspector = Inspector.InspectorForType(type);

            EncoderInfo encoderInfo;
            if (this.TryGetEncoder(type, out encoderInfo))
            {
                this.Encode(encoderInfo, value, true);
                return;
            }

            if (inspector.IsEnum)
            {
                ////TODO needs to be declared as enum - deserialize to object!!!!
                type = inspector.EnumType;

                if (this.TryGetEncoder(type, out encoderInfo))
                {
                    this.Encode(encoderInfo, value, true);
                    return;
                }
            }

            if (inspector.IsArray)
            {
                this.WriteArray(serializeType, type, (Array)value);
                return;
            }

            if (inspector.IsKeyValuePair)
            {
                this.WriteKeyValuePair(type, value);
                return;
            }

            if (inspector.IsTuple)
            {
                this.WriteTuple(type, value);
                return;
            }

            if (inspector.IsSerializable)
            {
                this.WriteSerializationInfo(inspector, serializeType, type, value);
                return;
            }

            // the inspector belongs to value.GetType(), so its type level flags answer exactly what
            // the interface casts answered, without the per object type checks
            if (inspector.ImplementsDictionary)
            {
                this.WriteDictionary(serializeType, type, (IDictionary)value);
                return;
            }

            var enumerable = inspector.ImplementsEnumerable ? (IEnumerable)value : null;

            ////TODO make better?
            if (enumerable != null && !inspector.HasProperties && (inspector.IsCollection || type.DeclaringType == LinqEnumerableType))
            {
                this.WriteEnumerable(inspector, serializeType, type, enumerable);
                return;
            }

            this.WriteObject(inspector, serializeType, type, value);
        }

        /// <param name="type">
        ///     The object type.
        /// </param>
        /// <param name="value">
        ///     The object to write.
        /// </param>
        internal virtual void WriteObject(Inspector inspector, Type serializeType, Type type, object value)
        {
            if (this.WriteOrAddObjectRef(value))
            {
                return;
            }

            if (this.configuration.EnableSerializationCallbacks)
            {
                inspector.OnSerializing?.Invoke(value, streamingContext);
            }

            this.WriteObjectTrailer(type, value, inspector).WriteObject(this, value);

            if (this.configuration.EnableSerializationCallbacks)
            {
                inspector.OnSerialized?.Invoke(value, streamingContext);
            }
        }

        /// <param name="type">
        ///     The object type.
        /// </param>
        /// <param name="value">
        ///     The object to write.
        /// </param>
        internal TypeSchema WriteObjectTrailer(Type type, object value, Inspector inspector)
        {
            Encoder.WriteTag(this.binaryWriter, Tags.Object);
            this.WriteType(type, value);
            return this.WriteTypeSchema(type, inspector);
        }

        /// <param name="type">
        ///     The object type.
        /// </param>
        /// <param name="value">
        ///     The object to write.
        /// </param>
        internal virtual void WriteSerializationInfo(Inspector inspector, Type serializeType, Type type, object value)
        {
            if (this.WriteOrAddObjectRef(value))
            {
                return;
            }

            var serializable = inspector.Serializable;
            var info = new SerializationInfo(serializable.InspectedType, new FormatterConverter());
            var streamingContext = new StreamingContext();
            ((ISerializable)value).GetObjectData(info, streamingContext);

            if (this.configuration.EnableSerializationCallbacks)
            {
                inspector.OnSerializing?.Invoke(value, streamingContext);
            }

            Encoder.WriteTag(this.binaryWriter, Tags.SerializationInfo);
            this.WriteType(type, value);
            Encoder.WriteCount(this.binaryWriter, info.MemberCount);
            var enumerator = info.GetEnumerator();
            while (enumerator.MoveNext())
            {
                this.WriteString(enumerator.Name);
                value = enumerator.Value;
                this.WriteType(enumerator.ObjectType, value);
                this.WriteObject(value);
            }

            if (this.configuration.EnableSerializationCallbacks)
            {
                inspector.OnSerialized?.Invoke(value, streamingContext);
            }
        }

        protected void Write(Type serializeType, object value)
        {
            if (value == null)
            {
                Encoder.WriteTag(this.binaryWriter, Tags.Null);
            }
            else
            {
                this.Write(serializeType, value.GetType(), value);
            }
        }

        /// <returns>
        ///     <c>true</c> if an object reference has been written, otherwise <c>false</c>.
        /// </returns>
        protected bool WriteOrAddObjectRef(object value)
        {
            // Avoid extensive adding of value types to the identity dictionary
            if (!value.GetType().IsValueType())
            {
                if (this.WriteOrAddRef(this.IdentityRefs, value, this.identityValue, Tags.ObjectRef))
                {
                    return true;
                }
            }

            ++this.identityValue;

            return false;
        }

        /// <param name="value">
        ///     The object.
        /// </param>
        /// <returns>
        ///     <c>true</c> if a string reference has been written, otherwise <c>false</c>.
        /// </returns>
        protected bool WriteOrAddStringRef(string value)
        {
#if NET8_0_OR_GREATER
            return this.WriteOrAddRef(this.StringRefs, value, this.StringRefs.Count, Tags.StringRef);
#else
            var stringRefs = this.StringRefs;
            int valueref;
            if (stringRefs.TryGetValue(value, out valueref))
            {
                Encoder.WriteTag(this.binaryWriter, Tags.StringRef);
                Encoder.WriteCount(this.binaryWriter, valueref);
                return true;
            }

            stringRefs.Add(value, stringRefs.Count);
            return false;
#endif
        }

        /// <returns>
        ///     <c>true</c> if a type reference has been written, otherwise <c>false</c>.
        /// </returns>
        protected bool WriteOrAddTypeRef(Type type)
        {
            return this.WriteOrAddRef(this.TypeRefs, type, this.TypeRefs.Count, Tags.TypeRef);
        }

        protected void WriteString(string value)
        {
            if (!string.IsNullOrEmpty(value) && this.WriteOrAddStringRef(value))
            {
                return;
            }

            Encoder.WriteString(this.binaryWriter, value, true);
        }

        protected void WriteType(Type type, object value)
        {
            if (!Encoder.TryWriteTypeCode(this.binaryWriter, type, true))
            {
                if (this.WriteOrAddTypeRef(type))
                {
                    return;
                }

                Encoder.WriteType(this.binaryWriter, type, value, this.configuration, true);
            }
        }

        /// <summary>
        ///     Determine if the collection is typed collection or not.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the collection is typed collection, otherwise <c>false</c>.
        /// </returns>
        private static CollectionFlags GetCollectionFlags(Type serializeType, Type type)
        {
            var flags = CollectionFlags.None;

            if (serializeType == typeof(object) || serializeType.IsInterface || serializeType.IsAbstract)
            {
                if (type.IsGenericType)
                {
                    var baseType = type.GetGenericTypeDefinition();
                    if (baseType != typeof(List<>) && baseType != typeof(HashSet<>))
                    {
                        flags |= CollectionFlags.Typed;
                    }
                    else if (!typeof(IList).IsAssignableFrom(type))
                    {
                        var setType = typeof(ISet<>).MakeGenericType(type.GetGenericArguments());
                        if (setType.IsAssignableFrom(type))
                        {
                            flags |= CollectionFlags.Set;
                        }
                    }
                }
                else if (type != typeof(ArrayList))
                {
                    flags |= CollectionFlags.Typed;
                }
            }

            return flags;
        }

        /// <summary>
        ///     Determine if the dictionary is typed dictionary or not.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the dictionary is typed dictionary, otherwise <c>false</c>.
        /// </returns>
        private static DictionaryFlags GetDictionaryFlags(Type serializeType, Type type)
        {
            var flags = DictionaryFlags.None;

            if (serializeType == typeof(object) || serializeType.IsInterface || serializeType.IsAbstract)
            {
                if (type.IsGenericType)
                {
                    var baseType = type.GetGenericTypeDefinition();
                    if (baseType != typeof(Dictionary<,>))
                    {
                        flags |= DictionaryFlags.Typed;
                    }
                }
            }

            return flags;
        }

        private static void Write(Serializer serializer, Type serializeType, object value, Func<object, object> getter)
        {
            serializer.Write(serializeType, getter(value));
        }

        private static void WriteCount(Serializer serializer, int value)
        {
            Encoder.WriteCount(serializer.binaryWriter, value);
        }

        private static void WriteType(Serializer serializer, Type type)
        {
            serializer.WriteType(type, null);
        }

        /// <summary>
        ///     Attempts to get the encoder info associated with the type.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the decoder info exists for the tag specified, otherwise <c>false</c>.
        /// </returns>
        private bool TryGetEncoder(Type type, out EncoderInfo encoderInfo)
        {
            if (this.encoderInfos.TryGetValue(type, out encoderInfo))
            {
                return true;
            }

            if (Encoder.TryGetEncoder(type, out encoderInfo))
            {
                this.encoderInfos.Add(type, encoderInfo);
                return true;
            }

            return false;
        }

        /// <param name="value">
        ///     The array to write.
        /// </param>
        private void WriteArray(Type serializeType, Type type, Array value)
        {
            if (this.WriteOrAddObjectRef(value))
            {
                return;
            }

            var elementType = type.GetElementType();

            // Dedicated handling of trivial byte arrays (performance)
            if (elementType == typeof(byte) && value.Rank == 1)
            {
                Encoder.WriteTag(this.binaryWriter, Tags.Byte | Tags.Array);
                // an array can have a maximum of 32 dimensions
                Encoder.WriteCount(this.binaryWriter, 1 | (byte)ArrayFlags.ValueNotTagged);
                Encoder.WriteCount(this.binaryWriter, value.Length);
                this.binaryWriter.Write((byte[])value);
            }
            else
            {
                EncoderInfo encoderInfo;
                if (this.TryGetEncoder(elementType, out encoderInfo))
                {
                    var hasElementTag = HasElementTag(encoderInfo.Tag, elementType);

                    Encoder.WriteTag(this.binaryWriter, encoderInfo.Tag | Tags.Array);
                    // an array can have a maximum of 32 dimensions
                    Encoder.WriteCount(this.binaryWriter,
                        value.Rank | (byte)(hasElementTag ? ArrayFlags.ValueTagged : ArrayFlags.ValueNotTagged));
                    for (var dimension = 0; dimension < value.Rank; dimension++)
                    {
                        Encoder.WriteCount(this.binaryWriter, value.GetLength(dimension));
                    }

                    if (elementType.IsSealed || elementType.IsPrimitive)
                    {
                        foreach (var item in value)
                        {
                            this.Encode(encoderInfo, item, hasElementTag);
                        }
                    }
                    else
                    {
                        EncoderInfo itemEncoderInfo = null;
                        var itemType = typeof(object);

                        foreach (var item in value)
                        {
                            if (item != null && item.GetType() != elementType)
                            {
                                if (itemEncoderInfo == null || itemType != item.GetType())
                                {
                                    if (this.TryGetEncoder(item.GetType(), out itemEncoderInfo))
                                    {
                                        this.Encode(itemEncoderInfo, item, hasElementTag);
                                        itemType = item.GetType();
                                    }
                                    else
                                    {
                                        this.Write(
                                            serializeType != typeof(object[]) && serializeType != typeof(object)
                                                ? elementType
                                                : typeof(object), item);
                                    }
                                }
                                else
                                {
                                    this.Encode(itemEncoderInfo, item, hasElementTag);
                                }
                            }
                            else
                            {
                                this.Encode(encoderInfo, item, hasElementTag);
                            }
                        }
                    }
                }
                else
                {
                    Encoder.WriteTag(this.binaryWriter, Tags.Object | Tags.Array);
                    Encoder.WriteCount(this.binaryWriter, value.Rank);
                    for (var dimension = 0; dimension < value.Rank; dimension++)
                    {
                        Encoder.WriteCount(this.binaryWriter, value.GetLength(dimension));
                    }

                    var itemType = serializeType != typeof(object[]) && serializeType != typeof(object)
                        ? elementType
                        : typeof(object);
                    foreach (var item in value)
                    {
                        this.Write(itemType, item);
                    }
                }
            }
        }

        /// <summary>
        ///     Write a date time value to the binary writer.
        /// </summary>
        /// <param name="bw">
        ///     The binary writer.
        /// </param>
        /// <param name="writeTag">
        ///     If <c>true</c> the encoder writes the leading type tag, otherwise <c>false</c>.
        /// </param>
        private static void WriteDateTimeOf(Serializer serializer, object value, bool writeTag)
        {
            Encoder.WriteDateTime(serializer.BinaryWriter, (DateTime)value, serializer.configuration, writeTag);
        }

        /// <param name="value">
        ///     The dictionary to write.
        /// </param>
        private void WriteDictionary(Type serializeType, Type type, IDictionary value)
        {
            if (this.WriteOrAddObjectRef(value))
            {
                return;
            }

            var keyType = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);
            var valueType = type.IsGenericType ? type.GetGenericArguments()[1] : typeof(object);

            Encoder.WriteTag(this.binaryWriter, Tags.Dictionary);

            var flags = GetDictionaryFlags(serializeType, type);

            EncoderInfo keyEncoderInfo;
            EncoderInfo valueEncoderInfo;
            if (this.TryGetEncoder(keyType, out keyEncoderInfo))
            {
                flags |= DictionaryFlags.KeyTypeTagged;
                var hasKeyElementTag = HasElementTag(keyEncoderInfo.Tag, keyType);
                flags |= hasKeyElementTag ? DictionaryFlags.KeyValueTagged : DictionaryFlags.None;

                if (this.TryGetEncoder(valueType, out valueEncoderInfo))
                {
                    flags |= DictionaryFlags.ValueTypeTagged;
                    var hasValueElementTag = HasElementTag(valueEncoderInfo.Tag, valueType);
                    flags |= hasValueElementTag ? DictionaryFlags.ValueTagged : DictionaryFlags.None;

                    Encoder.WriteByte(this.binaryWriter, (byte)flags, false);

                    if (((flags & DictionaryFlags.Typed) == DictionaryFlags.Typed))
                    {
                        this.WriteType(type, null);
                    }

                    Encoder.WriteTag(this.binaryWriter, keyEncoderInfo.Tag);
                    Encoder.WriteTag(this.binaryWriter, valueEncoderInfo.Tag);

                    Encoder.WriteCount(this.binaryWriter, value.Count);
                    if ((keyType.IsSealed || keyType.IsPrimitive) && (valueType.IsSealed || valueType.IsPrimitive))
                    {
                        foreach (DictionaryEntry item in value)
                        {
                            this.Encode(keyEncoderInfo, item.Key, hasKeyElementTag);
                            this.Encode(valueEncoderInfo, item.Value, hasValueElementTag);
                        }
                    }
                    else
                    {
                        EncoderInfo keyItemEncoderInfo = null;
                        var keyItemType = typeof(object);

                        EncoderInfo valueItemEncoderInfo = null;
                        var valueItemType = typeof(object);

                        foreach (DictionaryEntry item in value)
                        {
                            if (item.Key.GetType() != valueType)
                            {
                                if (keyItemEncoderInfo == null || keyItemType != item.Key.GetType())
                                {
                                    if (this.TryGetEncoder(item.Key.GetType(), out keyItemEncoderInfo))
                                    {
                                        this.Encode(keyItemEncoderInfo, item.Key, hasKeyElementTag);
                                        keyItemType = item.Key.GetType();
                                    }
                                    else
                                    {
                                        this.Write(valueType, item.Key);
                                    }
                                }
                                else
                                {
                                    this.Encode(keyItemEncoderInfo, item.Key, hasKeyElementTag);
                                }
                            }
                            else
                            {
                                this.Encode(keyEncoderInfo, item.Key, hasKeyElementTag);
                            }

                            if (item.Value != null && item.Value.GetType() != valueType)
                            {
                                if (valueItemEncoderInfo == null || valueItemType != item.Value.GetType())
                                {
                                    if (this.TryGetEncoder(item.Value.GetType(), out valueItemEncoderInfo))
                                    {
                                        this.Encode(valueItemEncoderInfo, item.Value, hasValueElementTag);
                                        valueItemType = item.Value.GetType();
                                    }
                                    else
                                    {
                                        this.Write(valueType, item.Value);
                                    }
                                }
                                else
                                {
                                    this.Encode(valueItemEncoderInfo, item.Value, hasValueElementTag);
                                }
                            }
                            else
                            {
                                this.Encode(valueEncoderInfo, item.Value, hasValueElementTag);
                            }
                        }
                    }
                }
                else
                {
                    if (serializeType == typeof(object))
                    {
                        flags |= DictionaryFlags.Typed;
                    }

                    Encoder.WriteByte(this.binaryWriter, (byte)flags, false);
                    if (((flags & DictionaryFlags.Typed) == DictionaryFlags.Typed))
                    {
                        this.WriteType(type, null);
                    }

                    Encoder.WriteTag(this.binaryWriter, keyEncoderInfo.Tag);
                    Encoder.WriteCount(this.binaryWriter, value.Count);
                    if (keyType.IsSealed || keyType.IsPrimitive)
                    {
                        foreach (DictionaryEntry item in value)
                        {
                            this.Encode(keyEncoderInfo, item.Key, hasKeyElementTag);
                            this.Write(valueType, item.Value);
                        }
                    }
                    else
                    {
                        EncoderInfo keyItemEncoderInfo = null;
                        var keyItemType = typeof(object);

                        foreach (DictionaryEntry item in value)
                        {
                            if (item.Key.GetType() != valueType)
                            {
                                if (keyItemEncoderInfo == null || keyItemType != item.Key.GetType())
                                {
                                    if (this.TryGetEncoder(item.Key.GetType(), out keyItemEncoderInfo))
                                    {
                                        this.Encode(keyItemEncoderInfo, item.Key, hasKeyElementTag);
                                        keyItemType = item.Key.GetType();
                                    }
                                    else
                                    {
                                        this.Write(valueType, item.Key);
                                    }
                                }
                                else
                                {
                                    this.Encode(keyItemEncoderInfo, item.Key, hasKeyElementTag);
                                }
                            }
                            else
                            {
                                this.Encode(keyEncoderInfo, item.Key, hasKeyElementTag);
                            }

                            this.Write(valueType, item.Value);
                        }
                    }
                }
            }
            else if (this.TryGetEncoder(valueType, out valueEncoderInfo))
            {
                flags |= DictionaryFlags.ValueTypeTagged;
                var hasValueElementTag = HasElementTag(valueEncoderInfo.Tag, valueType);
                flags |= hasValueElementTag ? DictionaryFlags.ValueTagged : DictionaryFlags.None;

                Encoder.WriteByte(this.binaryWriter, (byte)flags, false);
                if (((flags & DictionaryFlags.Typed) == DictionaryFlags.Typed))
                {
                    this.WriteType(type, null);
                }

                Encoder.WriteTag(this.binaryWriter, valueEncoderInfo.Tag);

                Encoder.WriteCount(this.binaryWriter, value.Count);
                if (valueType.IsSealed || valueType.IsPrimitive)
                {
                    foreach (DictionaryEntry item in value)
                    {
                        this.Write(keyType, item.Key);
                        this.Encode(valueEncoderInfo, item.Value, hasValueElementTag);
                    }
                }
                else
                {
                    EncoderInfo valueItemEncoderInfo = null;
                    var valueItemType = typeof(object);

                    foreach (DictionaryEntry item in value)
                    {
                        this.Write(keyType, item.Key);
                        if (item.Value != null && item.Value.GetType() != valueType)
                        {
                            if (valueItemEncoderInfo == null || valueItemType != item.Value.GetType())
                            {
                                if (this.TryGetEncoder(item.Value.GetType(), out valueItemEncoderInfo))
                                {
                                    this.Encode(valueItemEncoderInfo, item.Value, hasValueElementTag);
                                    valueItemType = item.Value.GetType();
                                }
                                else
                                {
                                    this.Write(valueType, item.Value);
                                }
                            }
                            else
                            {
                                this.Encode(valueItemEncoderInfo, item.Value, hasValueElementTag);
                            }
                        }
                        else
                        {
                            this.Encode(valueEncoderInfo, item.Value, hasValueElementTag);
                        }
                    }
                }
            }
            else
            {
                if (serializeType == typeof(object))
                {
                    flags |= DictionaryFlags.Typed;
                }

                Encoder.WriteByte(this.binaryWriter, (byte)flags, false);
                if (((flags & DictionaryFlags.Typed) == DictionaryFlags.Typed))
                {
                    this.WriteType(type, null);
                }

                Encoder.WriteCount(this.binaryWriter, value.Count);
                foreach (DictionaryEntry item in value)
                {
                    this.Write(keyType, item.Key);
                    this.Write(valueType, item.Value);
                }
            }
        }

        /// <param name="value">
        ///     The enumerable to write.
        /// </param>
        private void WriteEnumerable(Inspector inspector, Type serializeType, Type type, IEnumerable value)
        {
            if (this.WriteOrAddObjectRef(value))
            {
                return;
            }

            var elementType = inspector.Enumerable.ElementType;
            var hasCount = inspector.Enumerable.HasCount;
            if (hasCount)
            {
                Encoder.WriteTag(this.binaryWriter, Tags.Collection);

                var flags = GetCollectionFlags(serializeType, type);
                EncoderInfo encoderInfo;
                if (this.TryGetEncoder(elementType, out encoderInfo))
                {
                    flags |= CollectionFlags.TypeTagged;
                    var hasElementTag = HasElementTag(encoderInfo.Tag, elementType);
                    flags |= hasElementTag ? CollectionFlags.ValueTagged : CollectionFlags.None;

                    Encoder.WriteByte(this.binaryWriter, (byte)flags, false);

                    if (((flags & CollectionFlags.Typed) == CollectionFlags.Typed))
                    {
                        this.WriteType(type, null);
                    }

                    Encoder.WriteTag(this.binaryWriter, encoderInfo.Tag);

                    Encoder.WriteCount(this.binaryWriter, (int)inspector.Enumerable.Count(value));

                    if (elementType.IsSealed || elementType.IsPrimitive)
                    {
                        foreach (var item in value)
                        {
                            this.Encode(encoderInfo, item, hasElementTag);
                        }
                    }
                    else
                    {
                        EncoderInfo itemEncoderInfo = null;
                        var itemType = typeof(object);

                        foreach (var item in value)
                        {
                            if (item != null && item.GetType() != elementType)
                            {
                                if (itemEncoderInfo == null || itemType != item.GetType())
                                {
                                    if (this.TryGetEncoder(item.GetType(), out itemEncoderInfo))
                                    {
                                        this.Encode(itemEncoderInfo, item, hasElementTag);
                                        itemType = item.GetType();
                                    }
                                    else
                                    {
                                        this.Write(elementType, item);
                                    }
                                }
                                else
                                {
                                    this.Encode(itemEncoderInfo, item, hasElementTag);
                                }
                            }
                            else
                            {
                                this.Encode(encoderInfo, item, hasElementTag);
                            }
                        }
                    }

                    return;
                }

                if (serializeType == typeof(object))
                {
                    flags |= CollectionFlags.Typed;
                }

                Encoder.WriteByte(this.binaryWriter, (byte)flags, false);
                if (((flags & CollectionFlags.Typed) == CollectionFlags.Typed))
                {
                    this.WriteType(type, null);
                }

                Encoder.WriteCount(this.binaryWriter, (int)inspector.Enumerable.Count(value));
            }
            else
            {
                Encoder.WriteTag(this.binaryWriter, Tags.Enumerable);
            }

            foreach (var item in value)
            {
                this.Write(elementType, item);
            }

            if (!hasCount)
            {
                Encoder.WriteTag(this.binaryWriter, Tags.End);
            }
        }

        /// <param name="keyValuePair">
        ///     The key/value pair to write.
        /// </param>
        private void WriteKeyValuePair(Type type, object keyValuePair)
        {
            var typeargs = keyValuePair.GetType().GetGenericArguments();
            var propertyInfoKey = type.GetProperty(nameof(KeyValuePair<object, object>.Key));
            var keyGetter = DelegateFactory.CreateGetter(propertyInfoKey);
            var propertyInfoValue = type.GetProperty(nameof(KeyValuePair<object, object>.Value));
            var valueGetter = DelegateFactory.CreateGetter(propertyInfoValue);
            var write = typeof(Serializer).GetMethod(nameof(Write), BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(Serializer), typeof(Type), typeof(object), typeof(Func<object, object>) }, null);
            var writeType = typeof(Serializer).GetMethod(nameof(WriteType), BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(Serializer), typeof(Type) }, null);

            var serializerParameter = Expression.Parameter(typeof(Serializer), @"serializer");
            var keyValuePairParameter = Expression.Parameter(typeof(object), @"keyValuePair");
            var expressions = new List<Expression>
            {
                Expression.Call(null, writeType, serializerParameter, Expression.Constant(typeargs[0])),
                Expression.Call(null, writeType, serializerParameter, Expression.Constant(typeargs[1])),
                Expression.Call(null, write, serializerParameter, Expression.Constant(typeargs[0]),
                    keyValuePairParameter, Expression.Constant(keyGetter)),
                Expression.Call(null, write, serializerParameter, Expression.Constant(typeargs[1]),
                    keyValuePairParameter, Expression.Constant(valueGetter))
            };

            var block = Expression.Block(expressions);
            var serializer = Expression.Lambda<Serialize>(block, serializerParameter, keyValuePairParameter).Compile();
            Encoder.Register(type, Tags.KeyValuePair, serializer);
            Encoder.WriteTag(this.binaryWriter, Tags.KeyValuePair);
            serializer(this, keyValuePair);
        }

        /// <summary>
        ///     Writes or adds an reference.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if an object reference has been written, otherwise <c>false</c>.
        /// </returns>
        /// <typeparam name="TKey">
        ///     The key type.
        /// </typeparam>
        /// <typeparam name="TComparer">
        ///     The comparer type.
        /// </typeparam>
        private bool WriteOrAddRef<TKey, TComparer>(FlatDictionary<TKey, int, TComparer> dictionary, TKey key,
            int value, Tags tag)
            where TComparer : struct, IEqualityComparer<TKey>
        {
            int valueref;
            if (dictionary.TryGetOrAddValue(key, out valueref, value))
            {
                Encoder.WriteTag(this.binaryWriter, tag);
                Encoder.WriteCount(this.binaryWriter, valueref);
                return true;
            }

            return false;
        }

        /// <param name="tuple">
        ///     The tuple to write.
        /// </param>
        private void WriteTuple(Type type, object tuple)
        {
            var typeargs = tuple.GetType().GetGenericArguments();
            var write = typeof(Serializer).GetMethod(nameof(Write), BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(Serializer), typeof(Type), typeof(object), typeof(Func<object, object>) }, null);
            var writeType = typeof(Serializer).GetMethod(nameof(WriteType), BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(Serializer), typeof(Type) }, null);
            var writeCount = typeof(Serializer).GetMethod(nameof(WriteCount), BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(Serializer), typeof(int) }, null);

            var serializerParameter = Expression.Parameter(typeof(Serializer), @"serializer");
            var tupleParameter = Expression.Parameter(typeof(object), @"serializerParameter");
            var expressions = new List<Expression>
            {
                Expression.Call(null, writeCount, serializerParameter, Expression.Constant(typeargs.Length))
            };

            for (var i = 0; i < typeargs.Length; ++i)
            {
                var propertyInfoItem = type.GetProperty(@"Item" + (i + 1).ToString(CultureInfo.InvariantCulture));
                var itemGetter = DelegateFactory.CreateGetter(propertyInfoItem);
                expressions.Add(Expression.Call(null, writeType, serializerParameter,
                    Expression.Constant(typeargs[i])));
                expressions.Add(Expression.Call(null, write, serializerParameter, Expression.Constant(typeargs[i]),
                    tupleParameter, Expression.Constant(itemGetter)));
            }

            var block = Expression.Block(expressions);
            var serializer = Expression.Lambda<Serialize>(block, serializerParameter, tupleParameter).Compile();
            Encoder.Register(type, Tags.Tuple, serializer);
            Encoder.WriteTag(this.binaryWriter, Tags.Tuple);
            serializer(this, tuple);
        }

        /// <summary>
        ///     Write a type value to the binary writer.
        /// </summary>
        /// <param name="bw">
        ///     The binary writer.
        /// </param>
        /// <param name="writeTag">
        ///     If <c>true</c> the encoder writes the leading type tag, otherwise <c>false</c>.
        /// </param>
        private static void WriteTypeOf(Serializer serializer, object value, bool writeTag)
        {
            Encoder.WriteType(serializer.BinaryWriter, (Type)value, null, serializer.configuration, writeTag);
        }

        /// <returns>
        ///     The type schema written.
        /// </returns>
        private TypeSchema WriteTypeSchema(Type type, Inspector inspector)
        {
            TypeSchemaRef typeSchemaRef;
            if (this.TypeSchemaRefs.TryGetValue(type, out typeSchemaRef))
            {
                Encoder.WriteTag(this.binaryWriter, Tags.TypeSchemaRef);
                Encoder.WriteCount(this.binaryWriter, typeSchemaRef.Ref);
                return typeSchemaRef.TypeSchema;
            }

            var typeSchema = this.GetTypeSchema(type, inspector);
            this.binaryWriter.Write(typeSchema.SerializedSchema);

            typeSchemaRef.TypeSchema = typeSchema;
            typeSchemaRef.Ref = this.TypeSchemaRefs.Count;
            this.TypeSchemaRefs.Add(type, typeSchemaRef);
            return typeSchemaRef.TypeSchema;
        }

        private struct TypeSchemaRef
        {
            public int Ref;

            public TypeSchema TypeSchema;
        }
    }
}
