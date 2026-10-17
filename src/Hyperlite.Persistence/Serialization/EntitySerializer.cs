namespace Hyperlite.Persistence.Serialization
{
    using System;
    using System.Globalization;
    using System.IO;
    using Hyperlite.Persistence.Collections.Concurrent;
    using Hyperlite.Persistence.Reflection;
    using Hyperlite.Persistence.Serialization.Delegates;

    internal sealed class EntitySerializer : Serializer
    {
        private static readonly ConcurrentTypeDictionary<TypeSchema> TypeSchemaDictionary =
            new ConcurrentTypeDictionary<TypeSchema>();

        private readonly EntityContext entityContext;

        private readonly object root;

        private readonly SerializingEntity serializingEntity;

        /// <param name="serializingEntity">
        ///     The serializing entity delegate.
        /// </param>
        private EntitySerializer(EntityContext entityContext, BinaryWriter binaryWriter, object root,
            SerializingEntity serializingEntity)
            : base(binaryWriter, null, entityContext.Configuration.Context)
        {
            this.entityContext = entityContext;
            this.root = root;
            this.serializingEntity = serializingEntity;
        }

        /// <param name="value">
        ///     The object to serialize.
        /// </param>
        /// <param name="capacity">
        ///     The internal memory stream initial capacity.
        /// </param>
        /// <param name="write">
        ///     If <c>true</c> value will be serialized; otherwise only traversed, null will be returned.
        /// </param>
        /// <param name="serializingEntity">
        ///     The serializing entity delegate.
        /// </param>
        /// <returns>
        ///     The serialized object.
        /// </returns>
        /// <summary>
        ///     Serialize an object, handing the writer back so that the caller can pass the bytes on
        ///     without a managed copy.
        /// </summary>
        /// <param name="value">
        ///     The object to serialize.
        /// </param>
        /// <param name="serializingEntity">
        ///     The serializing entity delegate.
        /// </param>
        /// <returns>
        ///     The writer holding the serialized object, the caller has to dispose it.
        /// </returns>
        internal static HeapBinaryWriter SerializeToBuffer(EntityContext entityContext, Type serializeType,
            object value, SerializingEntity serializingEntity)
        {
            var binaryWriter = new HeapBinaryWriter();
            try
            {
                var entitySerializer = new EntitySerializer(entityContext, binaryWriter, value, serializingEntity);
                entitySerializer.Write(serializeType, value);
                return binaryWriter;
            }
            catch
            {
                binaryWriter.Dispose();
                throw;
            }
        }

        internal static byte[] Serialize(EntityContext entityContext, Type serializeType, object value, int capacity, bool write,
            SerializingEntity serializingEntity)
        {
            if (write)
            {
                using (var binaryWriter = new HeapBinaryWriter())
                {
                    var entitySerializer = new EntitySerializer(entityContext, binaryWriter, value, serializingEntity);
                    entitySerializer.Write(serializeType, value);
                    return binaryWriter.ToArray();
                }
            }

            using (var binaryWriter = new FakeBinaryWriter())
            {
                var entitySerializer = new EntitySerializer(entityContext, binaryWriter, value, serializingEntity);
                entitySerializer.Write(serializeType, value);
            }

            return null;
        }

        /// <inheritdoc />
        internal override void Encode(EncoderInfo encoderInfo, object value, bool writeTag)
        {
            if (value != null)
            {
                var anyType = value.GetType();
                if (encoderInfo.HandleObjectRef(anyType))
                {
                    if (this.WriteOrAddObjectRef(value))
                    {
                        return;
                    }

                    var inspector = Inspector.InspectorForType(anyType);
                    var entityReference = this.entityContext.EntityReferenceForInspector(inspector);

                    // Entity?
                    if (entityReference != null && this.SerializeEntityReference(entityReference, anyType, value))
                    {
                        return;
                    }
                }
            }

            encoderInfo.Encode(this, value, writeTag);
        }

        internal override TypeSchema GetTypeSchema(Type type, Inspector inspector)
        {
            return TypeSchemaDictionary.GetOrAdd(type, inspector, (t, i) => new TypeSchema(i, true));
        }

        /// <inheritdoc />
        internal override void WriteObject(Inspector inspector, Type serializeType, Type type, object value)
        {
            if (this.WriteOrAddObjectRef(value))
            {
                return;
            }

            var entityReference = this.entityContext.EntityReferenceForInspector(inspector);

            // Entity?
            if (entityReference != null && this.SerializeEntityReference(entityReference, serializeType, value))
            {
                return;
            }

            this.WriteObjectTrailer(type, value, inspector).WriteObject(this, value);
        }

        /// <returns>
        ///     <c>true</c> if no further encoding is required, otherwise <c>false</c>.
        /// </returns>
        private bool SerializeEntityReference(EntityReference entityReference, Type entityType, object entity)
        {
            var isRoot = ReferenceEquals(this.root, entity);
            var key = this.serializingEntity(isRoot, entityReference, entityType, entity);
            if (!isRoot)
            {
                if (key == null)
                {
                    bool generated;
                    key = entityReference.GetKeyFromEntity(entity, out generated);
                    if (generated)
                    {
                        throw new SerializationException(string.Format(CultureInfo.InvariantCulture,
                            @"Entity key has not been set for type {0}", entityType));
                    }
                }

                if (this.entityContext.StrictStaticTableBinding)
                {
                    if (this.entityContext.StrictStaticColumnBinding)
                    {
                        this.WriteEntityRow(entityReference, key);
                    }
                    else
                    {
                        this.WriteEntityKey(entityReference, key);
                    }
                }
                else
                {
                    this.WriteEntityReference(entityReference, key);
                }

                return true;
            }

            return key == null; // cancel further serialization
        }

        /// <param name="key">
        ///     The entity key.
        /// </param>
        private void WriteEntityKey(EntityReference entityReference, Key key)
        {
            var binaryWriter = this.BinaryWriter;
            Encoder.WriteTag(binaryWriter, Tags.EntityKey);
            this.WriteType(entityReference.EntityType, key);
            this.BinaryWriter.Write(key.Row);
            this.WriteString(key.ColumnFamily);
            this.WriteString(key.ColumnQualifier);
        }

        /// <param name="key">
        ///     The entity key.
        /// </param>
        private void WriteEntityReference(EntityReference entityReference, Key key)
        {
            var binaryWriter = this.BinaryWriter;
            Encoder.WriteTag(binaryWriter, Tags.EntityRef);
            this.WriteType(entityReference.EntityType, key);
            this.WriteString(entityReference.Namespace);
            this.WriteString(entityReference.TableName);
            this.BinaryWriter.Write(key.Row);
            this.WriteString(key.ColumnFamily);
            this.WriteString(key.ColumnQualifier);
        }

        /// <param name="key">
        ///     The entity key.
        /// </param>
        private void WriteEntityRow(EntityReference entityReference, Key key)
        {
            var binaryWriter = this.BinaryWriter;
            Encoder.WriteTag(binaryWriter, Tags.EntityRow);
            this.WriteType(entityReference.EntityType, key);
            this.BinaryWriter.Write(key.Row);
        }
    }
}
