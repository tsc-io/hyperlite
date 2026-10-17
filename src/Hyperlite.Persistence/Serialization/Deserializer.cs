namespace Hyperlite.Persistence.Serialization
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text;
    using Hyperlite.Persistence.Collections;
    using Hyperlite.Persistence.Extensions;
    using Hyperlite.Persistence.Reflection;
#if !HYPERLITE_SERIALIZATION
    using Hyperlite.Persistence.Scanner;

#endif

    public class Deserializer : SerializationBase
    {
        /// <summary>
        ///     Chunk size of the per deserialization reference collections.
        /// </summary>
        /// <remarks>
        ///     This is the size of the chunks after the first one - ChunkedCollection starts
        ///     with a chunk of 32, so nothing below 33 references touches this setting at all.
        ///     Above it the only question is how coarse a chunk should be, since a chunk sized
        ///     for n costs the same whether one reference lands in it or n do.
        ///     <para>
        ///     Measured at 256, 128 and 64 against graphs of 16 to 1,000 distinct references:
        ///     below 33 all three are identical; at 40 references 64 allocates a quarter less
        ///     than 256; at 100 and 300 it is still ahead; only past roughly a thousand does
        ///     256 win, and then by about 1%. Reference counts cluster at the small end, so
        ///     the case just over the first chunk is worth more than the case far beyond it.
        ///     </para>
        /// </remarks>
        /// <summary>
        ///     The encoding the stream overloads read strings with.
        /// </summary>
        /// <remarks>
        ///     Named rather than left to BinaryReader's default for two reasons. There is no
        ///     BinaryReader overload taking leaveOpen and the default encoding, and the
        ///     default is not the same everywhere: .NET Framework replaces invalid UTF-8
        ///     with U+FFFD and .NET throws. The byte-array path never used BinaryReader at
        ///     all - BinaryArrayReader decodes with these same settings - so on .NET
        ///     Framework a database read through a stream used to substitute characters that
        ///     reading the same bytes through an array rejected.
        ///     <para>
        ///     Throwing is the right half of that: the writer will not emit invalid UTF-8,
        ///     so bytes that are not valid mean a corrupt database, and saying so beats
        ///     inventing characters.
        ///     </para>
        /// </remarks>
        private static readonly UTF8Encoding StreamEncoding = new UTF8Encoding(false, true);

        private const int DefaultRefsCapacity = 64;

        /// <summary>
        ///     Initial capacity of the per deserialization type schema references.
        /// </summary>
        /// <remarks>
        ///     Unlike the chunked collections a List allocates its whole backing array up
        ///     front, so this size is paid in full the first time a schema is read - at 32
        ///     that was 312 bytes on every graph, however small. It holds one entry per
        ///     distinct entity type in the graph, which is a small number.
        ///     <para>
        ///     Measured at 32, 8 and 4: 8 saves 192 bytes on every graph up to 8 distinct
        ///     types and, because a List doubles rather than rehashes, is still ahead of 32
        ///     at 12 types. 4 saves 32 more but turns into a net loss there, so it buys the
        ///     last 32 bytes with the headroom.
        ///     </para>
        /// </remarks>
        private const int TypeSchemaRefsCapacity = 8;

        private readonly BinaryReader binaryReader;

        // The four back-reference collections, allocated on first use.
        //
        // A Deserializer is constructed per deserialized value, and these cost about 700
        // bytes between them - which is why deserializing a four byte int used to allocate
        // 1,187. A scalar refers to nothing, so it now allocates none of them.
        //
        // The format is unaffected: an absent collection and an empty one behave the same,
        // because the guards go through Count and that is zero either way.

        /// <summary>
        ///     The object references, or null until an object reference is read or recorded.
        /// </summary>
        private ChunkedCollection<object> objectRefs;

        /// <summary>
        ///     The string references, or null until a string reference is read or recorded.
        /// </summary>
        private ChunkedCollection<string> stringRefs;

        /// <summary>
        ///     The type references, or null until a type reference is read or recorded.
        /// </summary>
        private ChunkedCollection<Type> typeRefs;

        /// <summary>
        ///     The type schema references, or null until a schema is read or recorded.
        /// </summary>
        private List<TypeSchema> typeSchemaRefs;

        private StreamingContext streamingContext;

        protected Deserializer(BinaryReader binaryReader)
            : this(binaryReader, null)
        {
        }

        protected Deserializer(BinaryReader binaryReader, object streamingContext)
        {
            this.binaryReader = binaryReader;
            this.streamingContext = new StreamingContext(StreamingContextStates.Persistence, streamingContext);
        }

        public BinaryReader BinaryReader => this.binaryReader;

        /// <value>
        ///     The object references.
        /// </value>
        /// <summary>
        ///     Gets the object references, creating the collection on first use.
        /// </summary>
        /// <value>
        ///     The object references.
        /// </value>
        internal ChunkedCollection<object> ObjectRefs
        {
            get
            {
                return this.objectRefs
                       ?? (this.objectRefs = new ChunkedCollection<object>(DefaultRefsCapacity));
            }
        }

        /// <summary>
        ///     Gets the string references, creating the collection on first use.
        /// </summary>
        private ChunkedCollection<string> StringRefs
        {
            get
            {
                return this.stringRefs
                       ?? (this.stringRefs = new ChunkedCollection<string>(DefaultRefsCapacity));
            }
        }

        /// <summary>
        ///     Gets the type references, creating the collection on first use.
        /// </summary>
        private ChunkedCollection<Type> TypeRefs
        {
            get
            {
                return this.typeRefs
                       ?? (this.typeRefs = new ChunkedCollection<Type>(DefaultRefsCapacity));
            }
        }

        /// <summary>
        ///     Gets the type schema references, creating the list on first use.
        /// </summary>
        private List<TypeSchema> TypeSchemaRefs
        {
            get
            {
                return this.typeSchemaRefs
                       ?? (this.typeSchemaRefs = new List<TypeSchema>(TypeSchemaRefsCapacity));
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
        public static T Deserialize<T>(Stream stream)
        {
            // leaveOpen: the stream belongs to the caller. BinaryReader disposes it
            // otherwise, which closed it behind their back and made reading a second value
            // impossible
            using (var binaryReader = new BinaryStreamReader(stream, StreamEncoding))
            {
                return FromByteArray<T>(binaryReader);
            }
        }

        /// <summary>
        ///     Deserialize from the stream specified.
        /// </summary>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        public static object Deserialize(Type destinationType, Stream stream)
        {
            // leaveOpen, as above
            using (var binaryReader = new BinaryStreamReader(stream, StreamEncoding))
            {
                return FromByteArray(destinationType, binaryReader);
            }
        }

        /// <summary>
        ///     Deserialize from the byte array specified.
        /// </summary>
        /// <param name="serialized">
        ///     The serialized object.
        /// </param>
        /// <typeparam name="T">
        ///     Type of the object to deserialize.
        /// </typeparam>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        public static T FromByteArray<T>(byte[] serialized)
        {
            using (var binaryReader = new BinaryArrayReader(serialized))
            {
                return FromByteArray<T>(binaryReader);
            }
        }

        /// <summary>
        ///     Deserialize from the byte array specified.
        /// </summary>
        /// <param name="serialized">
        ///     The serialized object.
        /// </param>
        /// <param name="index">
        ///     The index in the data at which deserialization begins
        /// </param>
        /// <typeparam name="T">
        ///     Type of the object to deserialize.
        /// </typeparam>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        public static T FromByteArray<T>(byte[] serialized, int index)
        {
            using (var binaryReader = new BinaryArrayReader(serialized, index))
            {
                return FromByteArray<T>(binaryReader);
            }
        }

        /// <summary>
        ///     Deserialize from the byte array specified.
        /// </summary>
        /// <param name="serialized">
        ///     The serialized object.
        /// </param>
        /// <param name="index">
        ///     The index in the data at which deserialization begins
        /// </param>
        /// <param name="count">
        ///     The number of bytes to deserialize.
        /// </param>
        /// <typeparam name="T">
        ///     Type of the object to deserialize.
        /// </typeparam>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        public static T FromByteArray<T>(byte[] serialized, int index, int count)
        {
            using (var binaryReader = new BinaryArrayReader(serialized, index, count))
            {
                return FromByteArray<T>(binaryReader);
            }
        }

        /// <summary>
        ///     Deserialize from the byte array specified.
        /// </summary>
        /// <param name="serialized">
        ///     The serialized object.
        /// </param>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        public static object FromByteArray(Type destinationType, byte[] serialized)
        {
            using (var binaryReader = new BinaryArrayReader(serialized))
            {
                return new Deserializer(binaryReader).Deserialize(destinationType, Decoder.ReadTag(binaryReader));
            }
        }

        /// <summary>
        ///     Deserialize from the byte array specified.
        /// </summary>
        /// <param name="serialized">
        ///     The serialized object.
        /// </param>
        /// <param name="index">
        ///     The index in the data at which deserialization begins
        /// </param>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        public static object FromByteArray(Type destinationType, byte[] serialized, int index)
        {
            using (var binaryReader = new BinaryArrayReader(serialized, index))
            {
                return new Deserializer(binaryReader).Deserialize(destinationType, Decoder.ReadTag(binaryReader));
            }
        }

        /// <summary>
        ///     Deserialize from the byte array specified.
        /// </summary>
        /// <param name="serialized">
        ///     The serialized object.
        /// </param>
        /// <param name="index">
        ///     The index in the data at which deserialization begins
        /// </param>
        /// <param name="count">
        ///     The number of bytes to deserialize.
        /// </param>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        public static object FromByteArray(Type destinationType, byte[] serialized, int index, int count)
        {
            using (var binaryReader = new BinaryArrayReader(serialized, index, count))
            {
                return new Deserializer(binaryReader).Deserialize(destinationType, Decoder.ReadTag(binaryReader));
            }
        }

        /// <summary>
        ///     Reads an object.
        /// </summary>
        /// <typeparam name="T">
        ///     Type of the object to read.
        /// </typeparam>
        /// <returns>
        ///     The object read.
        /// </returns>
        public T ReadObject<T>()
        {
            var value = this.ReadObject(typeof(T));
            try
            {
                return (T)value;
            }
            catch (InvalidCastException e)
            {
#if!HYPERLITE_SERIALIZATION

                if (value is EntitySpec)
                {
                    throw new SerializationException(
                        string.Format(CultureInfo.InvariantCulture,
                            @"Unable to resolve entity reference for {0} , consider deferred reading", typeof(T)),
                        e);
                }

#endif

                throw new SerializationException(
                    string.Format(CultureInfo.InvariantCulture, @"Invalid cast {0} to {1}", value.GetType(), typeof(T)),
                    e);
            }
        }

        public object ReadObject(Type type)
        {
            return this.Deserialize(type, Decoder.ReadTag(this.binaryReader));
        }

        /// <summary>
        ///     Reads an object.
        /// </summary>
        /// <typeparam name="T">
        ///     Type of the object to read.
        /// </typeparam>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="action" /> is null.
        /// </exception>
        public void ReadObject<T>(Action<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var tag = Decoder.ReadTag(this.binaryReader);
            var value = this.Deserialize(typeof(T), tag);
            this.DeferredReadObject(tag, typeof(T), value, action);
        }

        /// <summary>
        ///     Before deserialize object properties notification.
        /// </summary>
        internal virtual void BeforeDeserializeObjectProperties(Type destinationType, Inspector inspector,
            object target)
        {
        }

        /// <summary>
        ///     Deserialize a property value.
        /// </summary>
        /// <returns>
        ///     The deserialized property value.
        /// </returns>
        internal object Deserialize(InspectedProperty inspectedProperty, Tags tag)
        {
            return this.Deserialize(inspectedProperty.PropertyType, tag);
        }

        internal virtual void ReadDictionaryItem(DictionaryFlags flags, Type keyType, Tags keyTag, Type valueType,
            Tags valueTag, Inspector inspector, IDictionary dictionary)
        {
            if (((flags & DictionaryFlags.KeyTypeTagged) != DictionaryFlags.KeyTypeTagged) || ((flags & DictionaryFlags.KeyValueTagged) == DictionaryFlags.KeyValueTagged))
            {
                keyTag = Decoder.ReadTag(this.binaryReader);
            }

            var key = this.Deserialize(keyType, keyTag);
            if (((flags & DictionaryFlags.ValueTypeTagged) != DictionaryFlags.ValueTypeTagged) || ((flags & DictionaryFlags.ValueTagged) == DictionaryFlags.ValueTagged))
            {
                valueTag = Decoder.ReadTag(this.binaryReader);
            }

            var value = this.Deserialize(valueType, valueTag);
            dictionary.Add(key, value);
        }

        internal virtual void ReadEnumerableItem(Type elementType, Tags tag, Inspector inspector, object collection,
            int pos, int count)
        {
            inspector.Enumerable.Fill(collection, this.Deserialize(elementType, tag));
        }

        /// <param name="value">
        ///     The property value.
        /// </param>
        internal virtual void SetObjectProperty(InspectedProperty inspectedProperty, object target, object value,
            Tags tag)
        {
            try
            {
                if (value == null && inspectedProperty.IsNotNullableValueType)
                {
                    value = Activator.CreateInstance(inspectedProperty.PropertyType, true);
                }

                inspectedProperty.Setter(target, value);
            }
            catch (Exception e)
            {
                throw new SerializationException(
                    string.Format(CultureInfo.InvariantCulture, "Set property value failed ({0}, {1}, {2})",
                        inspectedProperty.PropertyType, inspectedProperty.InspectedType, tag),
                    e);
            }
        }

        /// <summary>
        ///     Attempts to get the decoder info associated with the tag.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the decoder info exists for the tag specified, otherwise <c>false</c>.
        /// </returns>
        internal bool TryGetDecoder(Tags tag, out DecoderInfo decoderInfo)
        {
            // Decoder keeps the decoder infos of the low tags in an array, so the former per
            // instance dictionary in front of it only added a hash lookup and 32 buckets per
            // deserializer.
            return Decoder.TryGetDecoder(tag, out decoderInfo);
        }

        /// <summary>
        ///     Deserializes an object from the binary reader specified.
        /// </summary>
        /// <typeparam name="T">
        ///     The type to deserialize.
        /// </typeparam>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        protected static T FromByteArray<T>(BinaryReader binaryReader)
        {
            return new Deserializer(binaryReader).Deserialize<T>();
        }

        /// <summary>
        ///     Deserializes an object from the binary reader specified.
        /// </summary>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        protected static object FromByteArray(Type destinationType, BinaryReader binaryReader)
        {
            return new Deserializer(binaryReader).Deserialize(destinationType);
        }

        /// <summary>
        ///     The deferred read object.
        /// </summary>
        /// <typeparam name="T">
        ///     Type of object to read.
        /// </typeparam>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="action" /> in null.
        /// </exception>
        protected virtual void DeferredReadObject<T>(Tags tag, Type destinationType, object value, Action<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            action((T)value);
        }

        /// <summary>
        ///     Deserialize an object.
        /// </summary>
        /// <typeparam name="T">
        ///     Type of object to deserialize.
        /// </typeparam>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        protected T Deserialize<T>()
        {
            return (T)this.Deserialize(typeof(T));
        }

        /// <returns>
        ///     The deserialized object.
        /// </returns>
        protected object Deserialize(Type destinationType)
        {
            return this.Deserialize(destinationType, Decoder.ReadTag(this.binaryReader));
        }

        /// <returns>
        ///     The deserialized object.
        /// </returns>
        protected object Deserialize(Type destinationType, Tags tag)
        {
            // every nested value comes through here, so this is where too deep is caught: an
            // overflowed stack ends the process, this throws
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException e)
            {
                throw new SerializationException(@"The serialized value is nested too deeply to read", e);
            }

            // what the application asks for is declared, and so is what that declares
            var configuration = Encoder.Configuration;
            if (configuration.StrictTypes)
            {
                configuration.TypeAllowList.Declare(destinationType);
            }

            if ((tag & Tags.Array) > 0)
            {
                return this.ReadArray(destinationType, tag & ~Tags.Array);
            }

            return this.Read(destinationType, tag);
        }

        protected virtual object Read(Type destinationType, Tags tag)
        {
            object value;
            if (tag >= Tags.FirstDecoderTag && tag <= Tags.LastDecoderTag)
            {
                if (!Decoder.TryReadValue(this.binaryReader, tag, out value))
                {
                    throw new SerializationException(@"Unknown tag");
                }
            }
            else
            {
                if (destinationType == typeof(object))
                {
                    destinationType = Deserializer.TypeFromTag(tag);
                }

                switch (tag)
                {
                    case Tags.Null:
                        return null;

                    case Tags.Object:
                        value = this.ReadObj(destinationType);
                        break;

                    case Tags.ObjectRef:
                    {
                        var objref = Decoder.ReadCount(this.binaryReader);
                        if (objref < this.ObjectRefs.Count)
                        {
                            return this.ObjectRefs[objref];
                        }

                        throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                            @"Invalid object reference {0}", objref));
                    }

                    case Tags.SerializationInfo:
                        return this.ReadSerializationInfo(destinationType);

                    case Tags.TypeRef:
                    {
                        var typeref = Decoder.ReadCount(this.binaryReader);
                        if (typeref < this.TypeRefs.Count)
                        {
                            return this.TypeRefs[typeref];
                        }

                        throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                            @"Invalid type reference {0}", typeref));
                    }

                    case Tags.StringRef:
                    {
                        var stringref = Decoder.ReadCount(this.binaryReader);
                        if (stringref < this.StringRefs.Count)
                        {
                            return this.StringRefs[stringref];
                        }

                        throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                            @"Invalid string reference {0}", stringref));
                    }

                    case Tags.TypeSchema:
                    case Tags.TypeSchema2:
                    case Tags.TypeSchemaRef:
                        value = this.ReadTypeSchema(tag);
                        break;

                    case Tags.KeyValuePair:
                        value = this.ReadKeyValuePair(destinationType);
                        break;

                    case Tags.Tuple:
                        value = this.ReadTuple(destinationType);
                        break;

                    case Tags.Collection:
                    {
                        var flags = (CollectionFlags)Decoder.ReadByte(this.binaryReader);
                        value = ((flags & CollectionFlags.Typed) == CollectionFlags.Typed)
                            ? (((flags & CollectionFlags.TypeTagged) == CollectionFlags.TypeTagged)
                                ? this.ReadCollectionTypedTagged(destinationType, flags)
                                : this.ReadCollectionTyped(destinationType, flags))
                            : (((flags & CollectionFlags.TypeTagged) == CollectionFlags.TypeTagged)
                                ? this.ReadCollectionTagged(destinationType, flags)
                                : this.ReadCollection(destinationType, flags));
                        break;
                    }

                    case Tags.Enumerable:
                        value = this.ReadEnumerable(destinationType);
                        break;

                    case Tags.Dictionary:
                    {
                        var flags = (DictionaryFlags)Decoder.ReadByte(this.binaryReader);
                        value = ((flags & DictionaryFlags.Typed) == DictionaryFlags.Typed)
                            ? this.ReadDictionaryTyped(destinationType, flags)
                            : this.ReadDictionary(destinationType, flags);
                        break;
                    }

                    default:
                    {
                        DecoderInfo decoderInfo;
                        if (!this.TryGetDecoder(tag, out decoderInfo))
                        {
                            throw new SerializationException(@"Invalid tag");
                        }

                        var objref = this.ObjectRefs.Count;
                        this.ObjectRefs.Add(null); // TODO DOES NOT WORK in all cases
                        value = decoderInfo.Deserialize(this);
                        this.ObjectRefs[objref] = value;

                        if (value != null)
                        {
                            this.BeforeDeserializeObjectProperties(destinationType,
                                Inspector.InspectorForType(value.GetType()), value);
                        }

                        break;
                    }
                }
            }

            return destinationType.Convert(value);
        }

        protected object ReadArray(Type destinationType, Tags tag)
        {
            if (destinationType == null)
            {
                throw new ArgumentNullException(nameof(destinationType));
            }

            var rankAndFlags = Decoder.ReadCount(this.binaryReader);
            // an array can have a maximum of 32 dimensions
            var rank = rankAndFlags & (byte)ArrayFlags.RankMask;
            var lengths = new int[rank];
            long elements = 1;
            for (var dimension = 0; dimension < rank; ++dimension)
            {
                lengths[dimension] = Decoder.ReadCount(this.binaryReader);
                this.CheckItemCount(lengths[dimension]);
                elements *= lengths[dimension];
            }

            this.CheckItemCount(elements);

            if (destinationType.IsArray || destinationType == typeof(object))
            {
                var elementType = destinationType.IsArray
                    ? destinationType.GetElementType()
                    : Deserializer.TypeFromTag(tag);
                var hasElementTag = (rankAndFlags & (byte)ArrayFlags.ValueTagged) > 0
                                    || ((rankAndFlags & (byte)ArrayFlags.ValueNotTagged) == 0 &&
                                        LegacyHasElementTag(tag, elementType));

                if (rank == 1 && typeof(byte) == elementType && tag == Tags.Byte && !hasElementTag)
                {
                    var bytes = this.binaryReader.ReadBytes(lengths[0]);
                    this.ObjectRefs.Add(bytes);
                    return bytes;
                }

                if (rank == 1 && !hasElementTag)
                {
                    var values = this.ReadPrimitiveArray(elementType, tag, lengths[0]);
                    if (values != null)
                    {
                        this.ObjectRefs.Add(values);
                        return values;
                    }
                }

                var array = Array.CreateInstance(elementType, lengths);
                this.ObjectRefs.Add(array);
                this.ReadArrayElementsInRank(array, 0, new int[rank], elementType, tag, hasElementTag);
                return array;
            }

            var inspector = InspectorForEnumerable(destinationType);
            if (inspector.Enumerable.HasAdd)
            {
                var obj = inspector.CreateInstance();
                if (obj != null)
                {
                    this.ObjectRefs.Add(obj);
                    var length = lengths.Aggregate(1, (current, rankLength) => current * rankLength);
                    if (inspector.Enumerable.HasCapacity)
                    {
                        inspector.Enumerable.Capacity(obj, length);
                    }

                    var elementType = inspector.Enumerable.ElementType;
                    var hasElementTag = (rankAndFlags & (byte)ArrayFlags.ValueTagged) > 0
                                        || ((rankAndFlags & (byte)ArrayFlags.ValueNotTagged) == 0 &&
                                            LegacyHasElementTag(tag, elementType));

                    var target = inspector.Enumerable.CreateFillTarget(obj);
                    for (var n = 0; n < length; n++)
                    {
                        if (hasElementTag)
                        {
                            tag = Decoder.ReadTag(this.binaryReader);
                        }

                        this.ReadEnumerableItem(elementType, tag, inspector, target, n, length);
                    }

                    inspector.Enumerable.CompleteFill(obj, target);
                    return obj;
                }
            }

            throw new SerializationException(
                string.Format(CultureInfo.InvariantCulture,
                    @"Invalid destination type {0}, expecting IList, IList<T>, ISet<T> or Array", destinationType));
        }

        /// <summary>
        ///     The generic path deserializes every element into an object and stores it through
        ///     Array.SetValue, which boxes each value and pays a reflective, type checking store.
        ///     Only reachable for untagged elements, so every element carries the very same payload
        ///     its scalar reader expects, see Decoder.TryReadValue and Encoder.HasElementTag.
        /// </summary>
        /// <summary>
        ///     Reads an array of a primitive element type straight into a typed array.
        /// </summary>
        /// <param name="elementType">
        ///     The array element type.
        /// </param>
        /// <param name="tag">
        ///     The tag shared by all elements.
        /// </param>
        /// <param name="length">
        ///     The number of elements.
        /// </param>
        /// <returns>
        ///     The array, or null when the element type/tag pair is none of the primitives handled
        ///     here, in which case the caller stays on the generic path.
        /// </returns>
        private Array ReadPrimitiveArray(Type elementType, Tags tag, int length)
        {
            var reader = this.binaryReader;
            switch (tag)
            {
                case Tags.Bool:
                    if (elementType != typeof(bool))
                    {
                        return null;
                    }

                    {
                        var a = new bool[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadBool(reader);
                        }

                        return a;
                    }

                case Tags.SByte:
                    if (elementType != typeof(sbyte))
                    {
                        return null;
                    }

                    {
                        var a = new sbyte[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadSByte(reader);
                        }

                        return a;
                    }

                case Tags.Byte:
                    if (elementType != typeof(byte))
                    {
                        return null;
                    }

                    {
                        var a = new byte[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadByte(reader);
                        }

                        return a;
                    }

                case Tags.Short:
                    if (elementType != typeof(short))
                    {
                        return null;
                    }

                    {
                        var a = new short[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadShort(reader);
                        }

                        return a;
                    }

                case Tags.UShort:
                    if (elementType != typeof(ushort))
                    {
                        return null;
                    }

                    {
                        var a = new ushort[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadUShort(reader);
                        }

                        return a;
                    }

                case Tags.Int:
                    if (elementType != typeof(int))
                    {
                        return null;
                    }

                    {
                        var a = new int[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadInt(reader);
                        }

                        return a;
                    }

                case Tags.UInt:
                    if (elementType != typeof(uint))
                    {
                        return null;
                    }

                    {
                        var a = new uint[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadUInt(reader);
                        }

                        return a;
                    }

                case Tags.Long:
                    if (elementType != typeof(long))
                    {
                        return null;
                    }

                    {
                        var a = new long[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadLong(reader);
                        }

                        return a;
                    }

                case Tags.ULong:
                    if (elementType != typeof(ulong))
                    {
                        return null;
                    }

                    {
                        var a = new ulong[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadULong(reader);
                        }

                        return a;
                    }

                case Tags.Char:
                    if (elementType != typeof(char))
                    {
                        return null;
                    }

                    {
                        var a = new char[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadChar(reader);
                        }

                        return a;
                    }

                case Tags.Float:
                    if (elementType != typeof(float))
                    {
                        return null;
                    }

                    {
                        var a = new float[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadFloat(reader);
                        }

                        return a;
                    }

                case Tags.Double:
                    if (elementType != typeof(double))
                    {
                        return null;
                    }

                    {
                        var a = new double[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadDouble(reader);
                        }

                        return a;
                    }

                case Tags.Decimal:
                    if (elementType != typeof(decimal))
                    {
                        return null;
                    }

                    {
                        var a = new decimal[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadDecimal(reader);
                        }

                        return a;
                    }

                case Tags.DateTime:
                    if (elementType != typeof(DateTime))
                    {
                        return null;
                    }

                    {
                        var a = new DateTime[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadDateTime(reader);
                        }

                        return a;
                    }

                case Tags.DateTimeOffset:
                    if (elementType != typeof(DateTimeOffset))
                    {
                        return null;
                    }

                    {
                        var a = new DateTimeOffset[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadDateTimeOffset(reader);
                        }

                        return a;
                    }

                case Tags.TimeSpan:
                    if (elementType != typeof(TimeSpan))
                    {
                        return null;
                    }

                    {
                        var a = new TimeSpan[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadTimeSpan(reader);
                        }

                        return a;
                    }

                case Tags.Guid:
                    if (elementType != typeof(Guid))
                    {
                        return null;
                    }

                    {
                        var a = new Guid[length];
                        for (var i = 0; i < length; ++i)
                        {
                            a[i] = Decoder.ReadGuid(reader);
                        }

                        return a;
                    }

                default:
                    return null;
            }
        }
        protected virtual void ReadArrayElement(Type elementType, Tags tag, Array array, int[] indexes)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            array.SetValue(this.Deserialize(elementType, tag), indexes);
        }

        /// <summary>
        ///     Reads an array element in the array rank specified.
        /// </summary>
        protected void ReadArrayElementsInRank(Array array, int rank, int[] indexes, Type elementType, Tags tag,
            bool hasArrayElementTag)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (indexes == null)
            {
                throw new ArgumentNullException(nameof(indexes));
            }

            for (var i = 0; i < array.GetLength(rank); i++)
            {
                indexes[rank] = i;
                if (rank < array.Rank - 1)
                {
                    this.ReadArrayElementsInRank(array, rank + 1, indexes, elementType, tag, hasArrayElementTag);
                }
                else
                {
                    if (hasArrayElementTag)
                    {
                        tag = Decoder.ReadTag(this.binaryReader);
                    }

                    this.ReadArrayElement(elementType, tag, array, indexes);
                }
            }
        }

        protected object ReadCollection(Type destinationType, CollectionFlags flags)
        {
            if (destinationType == null)
            {
                throw new ArgumentNullException(nameof(destinationType));
            }

            var count = this.ReadItemCount();
            if (destinationType.IsArray)
            {
                var elementType = destinationType.GetElementType();
                var array = Array.CreateInstance(destinationType.GetElementType(), count);
                this.ObjectRefs.Add(array);
                for (var index = 0; index < count; ++index)
                {
                    var tag = Decoder.ReadTag(this.binaryReader);
                    array.SetValue(this.Deserialize(elementType, tag), index);
                }

                return array;
            }

            if (destinationType == typeof(object))
            {
                destinationType = ((flags & CollectionFlags.Set) == CollectionFlags.Set)
                    ? typeof(HashSet<>).MakeGenericType(typeof(object))
                    : typeof(List<>).MakeGenericType(typeof(object));
            }

            var inspector = InspectorForEnumerable(destinationType);
            if (inspector.Enumerable.HasAdd)
            {
                var obj = inspector.CreateInstance();
                if (obj != null)
                {
                    this.ObjectRefs.Add(obj);
                    if (inspector.Enumerable.HasCapacity)
                    {
                        inspector.Enumerable.Capacity(obj, count);
                    }

                    var elementType = inspector.Enumerable.ElementType;
                    var target = inspector.Enumerable.CreateFillTarget(obj);
                    for (var n = 0; n < count; ++n)
                    {
                        var tag = Decoder.ReadTag(this.binaryReader);
                        this.ReadEnumerableItem(elementType, tag, inspector, target, n, count);
                    }

                    inspector.Enumerable.CompleteFill(obj, target);
                    return obj;
                }
            }

            throw new SerializationException(
                string.Format(CultureInfo.InvariantCulture,
                    @"Invalid destination type {0}, expecting IList, IList<T>, ISet<T> or Array", destinationType));
        }

        protected object ReadCollectionTagged(Type destinationType, CollectionFlags flags)
        {
            if (destinationType == null)
            {
                throw new ArgumentNullException(nameof(destinationType));
            }

            var tag = Decoder.ReadTag(this.binaryReader);
            var count = this.ReadItemCount();
            if (destinationType.IsArray)
            {
                var elementType = destinationType.GetElementType();
                var array = Array.CreateInstance(elementType, count);
                this.ObjectRefs.Add(array);
                for (var index = 0; index < count; ++index)
                {
                    if (((flags & CollectionFlags.ValueTagged) == CollectionFlags.ValueTagged))
                    {
                        tag = Decoder.ReadTag(this.binaryReader);
                    }

                    array.SetValue(this.Deserialize(elementType, tag), index);
                }

                return array;
            }

            if (destinationType == typeof(object))
            {
                destinationType = ((flags & CollectionFlags.Set) == CollectionFlags.Set)
                    ? typeof(HashSet<>).MakeGenericType(Deserializer.TypeFromTag(tag))
                    : typeof(List<>).MakeGenericType(Deserializer.TypeFromTag(tag));
            }

            var inspector = InspectorForEnumerable(destinationType);
            if (inspector.Enumerable.HasAdd)
            {
                var obj = inspector.CreateInstance();
                if (obj != null)
                {
                    this.ObjectRefs.Add(obj);
                    if (inspector.Enumerable.HasCapacity)
                    {
                        inspector.Enumerable.Capacity(obj, count);
                    }

                    var elementType = inspector.Enumerable.ElementType;
                    var target = inspector.Enumerable.CreateFillTarget(obj);
                    for (var n = 0; n < count; ++n)
                    {
                        if (((flags & CollectionFlags.ValueTagged) == CollectionFlags.ValueTagged))
                        {
                            tag = Decoder.ReadTag(this.binaryReader);
                        }

                        this.ReadEnumerableItem(elementType, tag, inspector, target, n, count);
                    }

                    inspector.Enumerable.CompleteFill(obj, target);
                    return obj;
                }
            }

            throw new SerializationException(
                string.Format(CultureInfo.InvariantCulture,
                    @"Invalid destination type {0}, expecting  IList<>, IList, ISet<> or Array", destinationType));
        }

        protected object ReadCollectionTyped(Type destinationType, CollectionFlags flags)
        {
            var type = this.ReadType();
            return
                this.ReadCollection(
                    destinationType == typeof(object) || destinationType.IsInterface || destinationType.IsAbstract ||
                    !typeof(IEnumerable).IsAssignableFrom(destinationType)
                        ? type
                        : destinationType,
                    flags);
        }

        /// <returns>
        ///     The typed and tagged collection read.
        /// </returns>
        protected object ReadCollectionTypedTagged(Type destinationType, CollectionFlags flags)
        {
            if (destinationType == null)
            {
                throw new ArgumentNullException(nameof(destinationType));
            }

            var type = this.ReadType();
            return
                this.ReadCollectionTagged(
                    destinationType == typeof(object) || destinationType.IsInterface || destinationType.IsAbstract ||
                    !typeof(IEnumerable).IsAssignableFrom(destinationType)
                        ? type
                        : destinationType,
                    flags);
        }

        protected object ReadDictionary(Type destinationType, DictionaryFlags flags)
        {
            if (destinationType == null)
            {
                throw new ArgumentNullException(nameof(destinationType));
            }

            var tagKey = ((flags & DictionaryFlags.KeyTypeTagged) == DictionaryFlags.KeyTypeTagged) ? Decoder.ReadTag(this.binaryReader) : Tags.Null;
            var keyType = Deserializer.TypeFromTag(tagKey);

            var tagValue = ((flags & DictionaryFlags.ValueTypeTagged) == DictionaryFlags.ValueTypeTagged)
                ? Decoder.ReadTag(this.binaryReader)
                : Tags.Null;
            var valueType = Deserializer.TypeFromTag(tagValue);

            if (destinationType == typeof(object))
            {
                destinationType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
            }

            var inspector = InspectorForDictionary(destinationType);
            var obj = inspector.CreateInstance() as IDictionary;
            if (obj != null)
            {
                this.ObjectRefs.Add(obj);

                if (destinationType.IsGenericType)
                {
                    var genericArguments = destinationType.GetGenericArguments();
                    keyType = genericArguments[0];
                    valueType = genericArguments[1];
                }

                var count = this.ReadItemCount();
                for (var n = 0; n < count; ++n)
                {
                    this.ReadDictionaryItem(flags, keyType, tagKey, valueType, tagValue, inspector, obj);
                }

                return obj;
            }

            throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                @"Invalid destination type {0}, expecting IDictionary or IDictionary<,>", destinationType));
        }

        protected object ReadDictionaryTyped(Type destinationType, DictionaryFlags flags)
        {
            if (destinationType == null)
            {
                throw new ArgumentNullException(nameof(destinationType));
            }

            var type = this.ReadType();
            return
                this.ReadDictionary(
                    destinationType == typeof(object) || destinationType.IsInterface || destinationType.IsAbstract ||
                    !typeof(IDictionary).IsAssignableFrom(destinationType)
                        ? type
                        : destinationType,
                    flags);
        }

        protected object ReadEnumerable(Type destinationType)
        {
            if (destinationType == null)
            {
                throw new ArgumentNullException(nameof(destinationType));
            }

            if (destinationType == typeof(object))
            {
                destinationType = typeof(object[]);
            }

            if (destinationType.IsArray)
            {
                var elementType = destinationType.GetElementType();
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType), true);
                for (var tag = Decoder.ReadTag(this.binaryReader);
                    tag != Tags.End;
                    tag = Decoder.ReadTag(this.binaryReader))
                {
                    list.Add(this.Deserialize(elementType, tag));
                }

                var array = Array.CreateInstance(destinationType.GetElementType(), list.Count);
                this.ObjectRefs.Add(array);
                list.CopyTo(array, 0);
                return array;
            }

            var inspector = InspectorForEnumerable(destinationType);
            if (inspector.Enumerable.HasAdd)
            {
                var obj = inspector.CreateInstance();
                if (obj != null)
                {
                    this.ObjectRefs.Add(obj);
                    var elementType = inspector.Enumerable.ElementType;
                    var target = inspector.Enumerable.CreateFillTarget(obj);
                    var n = 0;
                    for (var tag = Decoder.ReadTag(this.binaryReader);
                        tag != Tags.End;
                        tag = Decoder.ReadTag(this.binaryReader))
                    {
                        this.ReadEnumerableItem(elementType, tag, inspector, target, n++, -1);
                    }

                    inspector.Enumerable.CompleteFill(obj, target);
                    return obj;
                }
            }

            throw new SerializationException(
                string.Format(CultureInfo.InvariantCulture,
                    @"Invalid destination type {0}, expecting IList, IList<T>, ISet<T> or Array", destinationType));
        }

        protected object ReadKeyValuePair(Type destinationType)
        {
            if (destinationType == null)
            {
                throw new ArgumentNullException(nameof(destinationType));
            }

            var genericArguments = new[] { this.ReadType(), this.ReadType() };
            var type = typeof(KeyValuePair<,>).MakeGenericType(genericArguments[0], genericArguments[1]);

            if (destinationType.IsGenericTypeDefinition(typeof(KeyValuePair<,>)))
            {
                if (!destinationType.IsAssignableFrom(type))
                {
                    type = destinationType;
                }
            }

            var ci = type.GetConstructor(genericArguments);
            return
                ci.Invoke(
                    new[]
                    {
                        this.Deserialize(genericArguments[0], Decoder.ReadTag(this.binaryReader)),
                        this.Deserialize(genericArguments[1], Decoder.ReadTag(this.binaryReader))
                    });
        }

        protected object ReadObj(Type destinationType)
        {
            if (destinationType == null)
            {
                throw new ArgumentNullException(nameof(destinationType));
            }

            Inspector inspector = null;
            object value = null;

            //// TODO call getter if real property

            // type
            var type = this.ReadType();
            if (destinationType.IsAssignableFrom(type) || destinationType.IsArray ||
                typeof(IList).IsAssignableFrom(destinationType))
            {
                inspector = Inspector.InspectorForType(type);
                value = inspector.CreateInstance();
            }

            if (value == null)
            {
                if (destinationType != typeof(object))
                {
                    inspector = Inspector.InspectorForType(destinationType);
                    value = inspector.CreateInstance();
                }

                if (value == null)
                {
                    value = Resolver.InstanceResolver(type, destinationType);

                    if (value == null)
                    {
                        throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                            @"Unable to create instance for type {0}/{1}", type, destinationType));
                    }
                }
            }

            if (inspector == null)
            {
                throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                    @"Unable to create inspector for type {0}/{1}", type, destinationType));
            }

            var callbacks = Encoder.Configuration.EnableSerializationCallbacks;
            if (callbacks)
            {
                inspector.OnDeserializing?.Invoke(value, streamingContext);
            }

            // type schema
            var typeSchema = this.ReadTypeSchema();
            this.ObjectRefs.Add(value);
            this.BeforeDeserializeObjectProperties(destinationType, inspector, value);

            var typeSchemaProperties = typeSchema.Properties;
            var length = typeSchemaProperties.Length;
            for (var i = 0; i < length; ++i)
            {
                // type schema will not be shared across types, therefore we can assign the inspected property
                var inspectedProperty = typeSchemaProperties[i].InspectedProperty
                                        ?? inspector.GetProperty(typeSchemaProperties[i].PropertyName, i);

                var tag = Decoder.ReadTag(this.binaryReader);
                if (inspectedProperty != null)
                {
                    this.SetObjectProperty(inspectedProperty, value, this.Deserialize(inspectedProperty, tag), tag);
                }
                else
                {
                    // Alternate property name
                    if (Resolver.PropertyNameResolver != null)
                    {
                        var resolvedName = Resolver.PropertyNameResolver(type, typeSchemaProperties[i].PropertyName);
                        inspectedProperty = inspector.GetProperty(resolvedName, typeSchemaProperties[i].PropertyName);
                        if (inspectedProperty != null)
                        {
                            // kept on this stream's schema, which belongs to this type alone,
                            // so the resolver is asked once per stream and nothing is recorded
                            // beyond it
                            typeSchemaProperties[i].InspectedProperty = inspectedProperty;
                            this.SetObjectProperty(
                                inspectedProperty,
                                value,
                                this.Deserialize(inspectedProperty, tag),
                                tag);

                            continue;
                        }
                    }

                    // Read unassignable values
                    Resolver.ObsoletePropertyResolver(value, typeSchemaProperties[i].PropertyName, this.Deserialize(typeof(object), tag));
                }
            }

            if (callbacks)
            {
                inspector.OnDeserialized?.Invoke(value, streamingContext);
            }

            return value;
        }

        protected object ReadSerializationInfo(Type destinationType)
        {
            if (destinationType == null)
            {
                throw new ArgumentNullException(nameof(destinationType));
            }

            Inspector inspector;
            var type = this.ReadType();
            if (destinationType.IsAssignableFrom(type))
            {
                inspector = Inspector.InspectorForType(type);
            }
            else if (destinationType != typeof(object))
            {
                inspector = Inspector.InspectorForType(destinationType);
            }
            else
            {
                throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                    @"Unable to create instance for type {0}", destinationType));
            }

            var serializable = inspector.Serializable;
            if (serializable == null)
            {
                throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                    @"Unable to create instance serialization info for type {0}", inspector.InspectedType));
            }

            var objref = this.ObjectRefs.Count;
            this.ObjectRefs.Add(null);

            var info = new SerializationInfo(serializable.InspectedType, new FormatterConverter());
            var count = this.ReadItemCount();
            for (var m = 0; m < count; ++m)
            {
                var k = this.ReadString();
                var v = this.ReadObject(this.ReadType());

                info.AddValue(k, v);
            }

            var streamingContext = new StreamingContext();
            var value = serializable.CreateInstance(info, streamingContext);
            this.ObjectRefs[objref] = value;
            if (value != null)
            {
                this.BeforeDeserializeObjectProperties(destinationType, inspector, value);
            }

            if (Encoder.Configuration.EnableSerializationCallbacks)
            {
                inspector.OnDeserializing?.Invoke(value, streamingContext);

                var deserializationCallback = value as IDeserializationCallback;
                deserializationCallback?.OnDeserialization(this);

                inspector.OnDeserialized?.Invoke(value, streamingContext);
            }

            return value;
        }

        protected string ReadString()
        {
            var tag = Decoder.ReadTag(this.binaryReader);
            switch (tag)
            {
                case Tags.Null:
                    return null;
                case Tags.StringEmpty:
                    return string.Empty;
                case Tags.String:
                    var value = Decoder.ReadString(this.binaryReader);
                    this.StringRefs.Add(value);
                    return value;
                case Tags.StringRef:
                    var stringref = Decoder.ReadCount(this.binaryReader);
                    if (stringref < this.StringRefs.Count)
                    {
                        return this.StringRefs[stringref];
                    }

                    throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                        @"Invalid string reference {0}", stringref));
            }

            throw new SerializationException(@"Deserialize string failed");
        }

        /// <returns>
        ///     The tuple to read.
        /// </returns>
        protected object ReadTuple(Type destinationType)
        {
            if (destinationType == null)
            {
                throw new ArgumentNullException(nameof(destinationType));
            }

            var size = this.ReadItemCount();
            var items = new KeyValuePair<Type, object>[size];
            for (var i = 0; i < size; ++i)
            {
                var type = this.ReadType();
                items[i] = new KeyValuePair<Type, object>(type,
                    this.Deserialize(type, Decoder.ReadTag(this.binaryReader)));
            }

            if (destinationType.IsTuple())
            {
                var typeArguments = items.Select(kv => kv.Key).ToArray();
                Type type;
                switch (size)
                {
                    case 1:
                        type = typeof(Tuple<>).MakeGenericType(typeArguments);
                        break;
                    case 2:
                        type = typeof(Tuple<,>).MakeGenericType(typeArguments);
                        break;
                    case 3:
                        type = typeof(Tuple<,,>).MakeGenericType(typeArguments);
                        break;
                    case 4:
                        type = typeof(Tuple<,,,>).MakeGenericType(typeArguments);
                        break;
                    case 5:
                        type = typeof(Tuple<,,,,>).MakeGenericType(typeArguments);
                        break;
                    case 6:
                        type = typeof(Tuple<,,,,,>).MakeGenericType(typeArguments);
                        break;
                    case 7:
                        type = typeof(Tuple<,,,,,,>).MakeGenericType(typeArguments);
                        break;
                    case 8:
                        type = typeof(Tuple<,,,,,,,>).MakeGenericType(typeArguments);
                        break;
                    default:
                        throw new SerializationException(@"Invalid item count for Tuple");
                }

                if (!destinationType.IsAssignableFrom(type))
                {
                    type = destinationType;
                }

                var ci = type.GetConstructor(typeArguments);
                return ci.Invoke(items.Select(kv => kv.Value).ToArray());
            }

            if (destinationType.IsGenericTypeDefinition(typeof(KeyValuePair<,>)) && size >= 2)
            {
                var type = typeof(KeyValuePair<,>).MakeGenericType(items[0].Key, items[1].Key);
                if (!destinationType.IsAssignableFrom(type))
                {
                    type = destinationType;
                }

                var ci = type.GetConstructor(new[] { items[0].Key, items[1].Key });
                return ci.Invoke(new[] { items[0].Value, items[1].Value });
            }

            throw new SerializationException(
                string.Format(CultureInfo.InvariantCulture,
                    @"Invalid destination type {0}, expecting Tuple<,[,]>, or KeyValuePair<,>", destinationType));
        }

        /// <returns>
        ///     The type read.
        /// </returns>
        /// <summary>
        ///     Reads a count of items, refusing one the input cannot hold.
        /// </summary>
        private int ReadItemCount()
        {
            var count = Decoder.ReadCount(this.binaryReader);
            this.CheckItemCount(count);
            return count;
        }

        /// <summary>
        ///     Refuses a count of items larger than the bytes left to read.
        /// </summary>
        /// <remarks>
        ///     Every item takes at least one byte - the smallest, a null, a bool or a zero,
        ///     take exactly one - so a count above what is left is truncated or crafted input,
        ///     and refusing it before anything is allocated is what stops a few bytes asking
        ///     for gigabytes. The bytes left are known for a byte array and a seekable stream;
        ///     a stream that cannot seek has no length to check against.
        /// </remarks>
        private void CheckItemCount(long count)
        {
            if (count < 0)
            {
                throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                    @"Invalid count {0}", count));
            }

            long remaining;
            var arrayReader = this.binaryReader as BinaryArrayReader;
            if (arrayReader != null)
            {
                remaining = arrayReader.Remaining;
            }
            else
            {
                var stream = this.binaryReader.BaseStream;
                if (stream == null || !stream.CanSeek)
                {
                    return;
                }

                remaining = stream.Length - stream.Position;
            }

            if (count > remaining)
            {
                throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                    @"The serialized value states {0} items where {1} bytes are left: every item takes at least one byte, so the input is truncated or not what this serializer wrote",
                    count,
                    remaining));
            }
        }

        protected Type ReadType()
        {
            var tag = Decoder.ReadTag(this.binaryReader);
            switch (tag)
            {
                case Tags.Null:
                    return null;
                case Tags.Type:
                    var type = Decoder.ReadCheckedType(this.binaryReader);
                    this.TypeRefs.Add(type);
                    return type;
                case Tags.TypeCode:
                    return Decoder.ReadTypeCode(this.binaryReader);
                case Tags.TypeRef:
                    var typeref = Decoder.ReadCount(this.binaryReader);
                    if (typeref < this.TypeRefs.Count)
                    {
                        return this.TypeRefs[typeref];
                    }

                    throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                        @"Invalid type reference {0}", typeref));
            }

            throw new SerializationException(@"Deserialize type failed");
        }

        /// <summary>
        ///     Gets an inspector for a dictionary type.
        /// </summary>
        private static Inspector InspectorForDictionary(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (type.IsInterface || type.IsAbstract)
            {
                if (type.IsGenericType)
                {
                    if (type.HasInterface(typeof(IDictionary<,>)) || typeof(IDictionary<,>).HasInterface(type))
                    {
                        type = typeof(Dictionary<,>).MakeGenericType(type.GetGenericArguments()[0],
                            type.GetGenericArguments()[1]);
                    }
                }
                else if (type.HasInterface(typeof(IDictionary)) || typeof(IDictionary).HasInterface(type))
                {
                    type = typeof(Dictionary<object, object>);
                }
            }

            return Inspector.InspectorForType(type);
        }

        /// <summary>
        ///     Gets an inspector for a enumerable type.
        /// </summary>
        private static Inspector InspectorForEnumerable(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (type.IsInterface || type.IsAbstract)
            {
                if (type.IsGenericType)
                {
                    if (type.HasInterface(typeof(IList<>)))
                    {
                        type = typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]);
                    }
                    else if (type.HasInterface(typeof(ISet<>)))
                    {
                        type = typeof(HashSet<>).MakeGenericType(type.GetGenericArguments()[0]);
                    }
                    else if (typeof(IList<>).HasInterface(type))
                    {
                        type = typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]);
                    }
                    else if (typeof(ISet<>).HasInterface(type))
                    {
                        type = typeof(HashSet<>).MakeGenericType(type.GetGenericArguments()[0]);
                    }
                }
                else if (type.HasInterface(typeof(IList)) || typeof(IList).HasInterface(type))
                {
                    type = typeof(ArrayList);
                }
            }

            return Inspector.InspectorForType(type);
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
        /// <remarks>
        ///     Required for reading old data, before v0.9.8.7
        /// </remarks>
        private static bool LegacyHasElementTag(Tags tag, Type type)
        {
            tag &= ~Tags.Array;
            return tag == Tags.Object || tag == Tags.Int || tag == Tags.UInt || tag == Tags.Long || tag == Tags.ULong ||
                   tag == Tags.Float || tag == Tags.Double
                   || (tag >= Tags.FirstCustomType && !type.IsSealed);
        }

        /// <summary>
        ///     Determine if the type from the tag specified.
        /// </summary>
        /// <param name="tag">
        ///     The type tag.
        /// </param>
        /// <returns>
        ///     The type or typeof(object).
        /// </returns>
        private static Type TypeFromTag(Tags tag)
        {
            tag &= ~Tags.Array;
            switch (tag)
            {
                case Tags.SByte:
                case Tags.SByte0:
                    return typeof(sbyte);
                case Tags.Byte:
                case Tags.Byte0:
                    return typeof(byte);
                case Tags.Short:
                case Tags.Short0:
                    return typeof(short);
                case Tags.UShort:
                case Tags.UShort0:
                    return typeof(ushort);
                case Tags.Int:
                case Tags.Int0:
                    return typeof(int);
                case Tags.UInt:
                case Tags.UInt0:
                    return typeof(uint);
                case Tags.Long:
                case Tags.Long0:
                    return typeof(long);
                case Tags.ULong:
                case Tags.ULong0:
                    return typeof(ulong);
                case Tags.Bool:
                case Tags.True:
                case Tags.False:
                    return typeof(bool);
                case Tags.Char:
                    return typeof(char);
                case Tags.Float:
                case Tags.Float0:
                case Tags.FloatNaN:
                    return typeof(float);
                case Tags.Double:
                case Tags.Double0:
                case Tags.DoubleNaN:
                    return typeof(double);
                case Tags.Decimal:
                    return typeof(decimal);
                case Tags.DateTime:
                case Tags.DateTime0:
                    return typeof(DateTime);
                case Tags.DateTimeOffset:
                case Tags.DateTimeOffset0:
                    return typeof(DateTimeOffset);
                case Tags.TimeSpan:
                    return typeof(TimeSpan);
                case Tags.String:
                case Tags.StringEmpty:
                    return typeof(string);
                case Tags.Guid:
                    return typeof(Guid);
                case Tags.Type:
                case Tags.TypeCode:
                    return typeof(Type).GetType();
                case Tags.Uri:
                    return typeof(Uri);
                case Tags.Null:
                case Tags.Object:
                    return typeof(object);
            }

            return tag >= Tags.FirstCustomType && Decoder.TryGetType(tag, out var type) ? type : typeof(object);
        }

        private TypeSchema ReadTypeSchema()
        {
            return this.ReadTypeSchema(Decoder.ReadTag(this.binaryReader));
        }

        /// <summary>
        ///     Reads the type schema for the tag specified.
        /// </summary>
        private TypeSchema ReadTypeSchema(Tags tag)
        {
            switch (tag)
            {
                case Tags.Null:
                    return null;
                case Tags.TypeSchema:
                {
                    var typeSchemaPropertyCount = this.ReadItemCount();
                    var typeSchemaProperties = new TypeSchemaProperty[typeSchemaPropertyCount];
                    for (var i = 0; i < typeSchemaPropertyCount; ++i)
                    {
                        typeSchemaProperties[i] = new TypeSchemaProperty { PropertyName = this.ReadString() };
                    }

                    var typeSchema = new TypeSchema(typeSchemaProperties);
                    this.TypeSchemaRefs.Add(typeSchema);
                    return typeSchema;
                }

                case Tags.TypeSchema2:
                {
                    switch (Decoder.ReadByte(this.binaryReader))
                    {
                        case TypeSchema.Version:
                            var typeSchemaPropertyCount = this.ReadItemCount();
                            var typeSchemaProperties = new TypeSchemaProperty[typeSchemaPropertyCount];
                            for (var i = 0; i < typeSchemaPropertyCount; ++i)
                            {
                                typeSchemaProperties[i] = new TypeSchemaProperty
                                {
                                    PropertyName = Decoder.ReadString(this.binaryReader)
                                };
                            }

                            var typeSchema = new TypeSchema(typeSchemaProperties);
                            this.TypeSchemaRefs.Add(typeSchema);
                            return typeSchema;
                    }

                    throw new SerializationException("Unsupported type schema");
                }

                case Tags.TypeSchemaRef:
                    var typeschemaref = Decoder.ReadCount(this.binaryReader);
                    if (typeschemaref < this.TypeSchemaRefs.Count)
                    {
                        return this.TypeSchemaRefs[typeschemaref];
                    }

                    throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                        @"Invalid type schema reference {0}", typeschemaref));
            }

            throw new SerializationException(@"Deserialize type schema failed");
        }
    }
}
