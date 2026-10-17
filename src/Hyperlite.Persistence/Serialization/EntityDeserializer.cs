namespace Hyperlite.Persistence.Serialization
{
    using System;
    using System.Collections;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using Hyperlite.Persistence.Extensions;
    using Hyperlite.Persistence.Reflection;
    using Hyperlite.Persistence.Scanner;
    using Hyperlite.Persistence.Serialization.Delegates;

    internal sealed class EntityDeserializer : Deserializer
    {
        private readonly DeserializingEntity deserializingEntity;

        private readonly EntityContext entityContext;

        private readonly EntityReader entityReader;

        private readonly EntityScanner entityScanner;

        /// <param name="deserializingEntity">
        ///     The deserializing entity delegate.
        /// </param>
        private EntityDeserializer(EntityReader entityReader, BinaryReader binaryReader,
            DeserializingEntity deserializingEntity)
            : base(binaryReader, entityReader.EntityScanner.EntityContext.Configuration.Context)
        {
            this.entityReader = entityReader;
            this.entityScanner = entityReader.EntityScanner;
            this.entityContext = this.entityScanner.EntityContext;
            this.deserializingEntity = deserializingEntity;
        }

        /// <param name="serialized">
        ///     The serialized object.
        /// </param>
        /// <param name="deserializingEntity">
        ///     The deserializing entity delegate.
        /// </param>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        internal static object Deserialize(EntityReader entityReader, Type destinationType, byte[] serialized,
            DeserializingEntity deserializingEntity)
        {
            using (var binaryReader = new BinaryArrayReader(serialized))
            {
                return new EntityDeserializer(entityReader, binaryReader, deserializingEntity).Deserialize(
                    destinationType, Decoder.ReadTag(binaryReader));
            }
        }

        /// <param name="buffer">
        ///     The serialized object.
        /// </param>
        /// <param name="length">
        ///     The buffer length.
        /// </param>
        /// <param name="deserializingEntity">
        ///     The deserializing entity delegate.
        /// </param>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        internal static object Deserialize(EntityReader entityReader, Type destinationType, IntPtr buffer, int length,
            DeserializingEntity deserializingEntity)
        {
            using (var binaryReader = new BinaryArrayReader(buffer, length))
            {
                return new EntityDeserializer(entityReader, binaryReader, deserializingEntity).Deserialize(
                    destinationType, Decoder.ReadTag(binaryReader));
            }
        }

        /// <param name="serialized">
        ///     The serialized object.
        /// </param>
        /// <param name="count">
        ///     The size of the serialized object.
        /// </param>
        /// <param name="deserializingEntity">
        ///     The deserializing entity delegate.
        /// </param>
        /// <returns>
        ///     The deserialized object.
        /// </returns>
        internal static object Deserialize(EntityReader entityReader, Type destinationType, byte[] serialized, int count,
            DeserializingEntity deserializingEntity)
        {
            using (var binaryReader = new BinaryArrayReader(serialized, 0, count))
            {
                return new EntityDeserializer(entityReader, binaryReader, deserializingEntity).Deserialize(
                    destinationType, Decoder.ReadTag(binaryReader));
            }
        }

        /// <inheritdoc />
        internal override void BeforeDeserializeObjectProperties(Type destinationType, Inspector inspector,
            object target)
        {
            var entityReference =
                inspector != null ? this.entityContext.EntityReferenceForInspector(inspector) : null;
            if (entityReference != null)
            {
                this.deserializingEntity(entityReference, destinationType, target);
            }
        }

        internal override void ReadDictionaryItem(
            DictionaryFlags flags,
            Type keyType,
            Tags keyTag,
            Type valueType,
            Tags valueTag,
            Inspector inspector,
            IDictionary dictionary)
        {
            if (((flags & DictionaryFlags.KeyTypeTagged) != DictionaryFlags.KeyTypeTagged) || ((flags & DictionaryFlags.KeyValueTagged) == DictionaryFlags.KeyValueTagged))
            {
                keyTag = Decoder.ReadTag(this.BinaryReader);
            }

            // A half that is an entity not fetched yet comes back as a placeholder and is
            // registered at once: registering takes the next object reference slot, which
            // has to happen before the other half is read, in the order they were written.
            PendingEntry pending = null;
            var key = this.Deserialize(keyType, keyTag);
            var keySpec = key as EntitySpec;
            if (keySpec != null)
            {
                pending = new PendingEntry(dictionary);
                pending.Await();
                this.AwaitEntity(keyType, keySpec, keyTag, pending.ResolveKey);
            }

            if (((flags & DictionaryFlags.ValueTypeTagged) != DictionaryFlags.ValueTypeTagged) || ((flags & DictionaryFlags.ValueTagged) == DictionaryFlags.ValueTagged))
            {
                valueTag = Decoder.ReadTag(this.BinaryReader);
            }

            var value = this.Deserialize(valueType, valueTag);
            var valueSpec = value as EntitySpec;
            if (valueSpec != null)
            {
                pending = pending ?? new PendingEntry(dictionary);
                pending.Await();
                this.AwaitEntity(valueType, valueSpec, valueTag, pending.ResolveValue);
            }

            if (pending == null)
            {
                dictionary.Add(key, value);
                return;
            }

            pending.Read(keySpec == null, key, valueSpec == null, value);
        }

        internal override void ReadEnumerableItem(Type elementType, Tags tag, Inspector inspector, object collection,
            int pos, int count)
        {
            var any = this.Deserialize(elementType, tag);
            var entitySpec = any as EntitySpec;
            if (entitySpec != null)
            {
                EntityScanTarget entityScanTarget;
                if (inspector.Enumerable.HasIndexer)
                {
                    inspector.Enumerable.Add(collection, null);
                    entityScanTarget = new EntityIndexerScanTarget(elementType, entitySpec, inspector.Enumerable,
                        collection, pos);
                }
                else
                {
                    entityScanTarget =
                        new EntityCollectionScanTarget(elementType, entitySpec, inspector.Enumerable, collection);
                }

                if (IsEntityReference(tag))
                {
                    this.entityScanner.Add(entityScanTarget);
                    this.ObjectRefs.Add(entityScanTarget);
                }
                else if (tag == Tags.ObjectRef)
                {
                    ((EntityScanTarget)entitySpec).AddScanTargetRef(entityScanTarget);
                }
            }
            else
            {
                inspector.Enumerable.Add(collection, any);
            }
        }

        /// <inheritdoc />
        internal override void SetObjectProperty(InspectedProperty inspectedProperty, object target, object value,
            Tags tag)
        {
            var entitySpec = value as EntitySpec;
            if (entitySpec != null)
            {
                var entityScanTarget = new EntityScanTarget(inspectedProperty, entitySpec, target);
                if (IsEntityReference(tag))
                {
                    this.entityScanner.Add(entityScanTarget);
                    this.ObjectRefs.Add(entityScanTarget);
                }
                else if (tag == Tags.ObjectRef)
                {
                    ((EntityScanTarget)entitySpec).AddScanTargetRef(entityScanTarget);
                }
            }
            else
            {
                base.SetObjectProperty(inspectedProperty, target, value, tag);
            }
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
        protected override void DeferredReadObject<T>(Tags tag, Type destinationType, object any, Action<T> action)
        {
            var entitySpec = any as EntitySpec;
            if (entitySpec != null)
            {
                var entityScanTarget =
                    new EntityScanTarget(destinationType, entitySpec, (target, value) => action((T)value));
                if (IsEntityReference(tag))
                {
                    this.entityScanner.Add(entityScanTarget);
                    this.ObjectRefs.Add(entityScanTarget);
                }
                else if (tag == Tags.ObjectRef)
                {
                    ((EntityScanTarget)entitySpec).AddScanTargetRef(entityScanTarget);
                }
            }
            else
            {
                base.DeferredReadObject(tag, destinationType, any, action);
            }
        }

        protected override object Read(Type destinationType, Tags tag)
        {
            EntitySpec entitySpec = null;
            switch (tag)
            {
                case Tags.EntityRef:
                    entitySpec = new EntitySpec(
                        this.ReadType(), this.ReadString(), this.ReadString(),
                        new Key(this.BinaryReader.ReadString(), this.ReadString(), this.ReadString()));
                    break;

                case Tags.EntityKey:
                {
                    var type = this.ReadType();
                    var entityReference = this.entityContext.EntityReferenceForType(type);
                    if (entityReference == null)
                    {
                        throw new PersistenceException(string.Format(CultureInfo.InvariantCulture,
                            @"{0} is not a valid entity", type));
                    }

                    var tableBinding = entityReference.TableBinding;
                    if (tableBinding == null)
                    {
                        throw new PersistenceException(string.Format(CultureInfo.InvariantCulture,
                            @"Undefined table binding for type {0}", type));
                    }

                    entitySpec = new EntitySpec(
                        type, tableBinding.Namespace, tableBinding.TableName,
                        new Key(this.BinaryReader.ReadString(), this.ReadString(), this.ReadString()));
                    break;
                }

                case Tags.EntityRow:
                {
                    var type = this.ReadType();
                    var entityReference = this.entityContext.EntityReferenceForType(type);
                    if (entityReference == null)
                    {
                        throw new PersistenceException(string.Format(CultureInfo.InvariantCulture,
                            @"{0} is not a valid entity", type));
                    }

                    var tableBinding = entityReference.TableBinding;
                    if (tableBinding == null)
                    {
                        throw new PersistenceException(string.Format(CultureInfo.InvariantCulture,
                            @"Undefined table binding for type {0}", type));
                    }

                    var columnBinding = entityReference.ColumnBinding;
                    if (columnBinding == null)
                    {
                        throw new PersistenceException(string.Format(CultureInfo.InvariantCulture,
                            @"Undefined column binding for type {0}", type));
                    }

                    entitySpec = new EntitySpec(
                        type, tableBinding.Namespace, tableBinding.TableName,
                        new Key(this.BinaryReader.ReadString(), columnBinding.ColumnFamily,
                            columnBinding.ColumnQualifier));
                    break;
                }
            }

            if (entitySpec != null)
            {
                object entity;
                if (this.entityReader.TryGetFetchedEntity(entitySpec, out entity))
                {
                    this.ObjectRefs.Add(entity);
                    return destinationType.Convert(entity);
                }

                return entitySpec;
            }

            return base.Read(destinationType, tag);
        }

        protected override void ReadArrayElement(Type elementType, Tags tag, Array array, int[] indexes)
        {
            var any = this.Deserialize(elementType, tag);
            var entitySpec = any as EntitySpec;
            if (entitySpec != null)
            {
                EntityScanTarget entityScanTarget = new EntityArrayScanTarget(elementType, entitySpec, array, indexes);
                if (IsEntityReference(tag))
                {
                    this.entityScanner.Add(entityScanTarget);
                    this.ObjectRefs.Add(entityScanTarget);
                }
                else if (tag == Tags.ObjectRef)
                {
                    ((EntityScanTarget)entitySpec).AddScanTargetRef(entityScanTarget);
                }
            }
            else
            {
                if (array == null)
                {
                    throw new ArgumentNullException(nameof(array));
                }

                array.SetValue(any, indexes);
            }
        }

        /// <summary>
        ///     Determines if the type tag is an entity reference.
        /// </summary>
        /// <param name="tag">
        ///     The type tag.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the type tag is an entity reference.
        /// </returns>
        private static bool IsEntityReference(Tags tag)
        {
            return tag == Tags.EntityRef || tag == Tags.EntityKey || tag == Tags.EntityRow;
        }

        /// <summary>
        ///     Registers a placeholder for an entity not fetched yet, whose setter runs once
        ///     the entity arrives - as every other container does for its own slots.
        /// </summary>
        /// <param name="entityType">
        ///     The declared type of the slot.
        /// </param>
        /// <param name="entitySpec">
        ///     The placeholder the deserializer returned.
        /// </param>
        /// <param name="tag">
        ///     The tag it was read with.
        /// </param>
        /// <param name="setter">
        ///     Called with the entity when it arrives.
        /// </param>
        private void AwaitEntity(Type entityType, EntitySpec entitySpec, Tags tag, Action<object, object> setter)
        {
            var entityScanTarget = new EntityScanTarget(entityType, entitySpec, setter);
            if (IsEntityReference(tag))
            {
                this.entityScanner.Add(entityScanTarget);
                this.ObjectRefs.Add(entityScanTarget);
            }
            else if (tag == Tags.ObjectRef)
            {
                ((EntityScanTarget)entitySpec).AddScanTargetRef(entityScanTarget);
            }
        }

        /// <summary>
        ///     A dictionary entry waiting for the entities it holds.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Added to the dictionary once every half that is an entity has arrived. It
        ///         starts with one count held for the reading itself, so it cannot be added
        ///         before both halves have been read, whenever the entities arrive.
        ///     </para>
        ///     <para>
        ///         An entity that no longer exists is never delivered, so an entry referring
        ///         to one, as its key or its value, is never added. Should a key be delivered
        ///         as null it is dropped too: a dictionary cannot hold a null key.
        ///     </para>
        ///     <para>
        ///         An entry waiting for an entity is added when that entity arrives, so a
        ///         dictionary that keeps insertion order can come back in a different one.
        ///     </para>
        /// </remarks>
        private sealed class PendingEntry
        {
            private readonly IDictionary dictionary;

            private object key;

            private object value;

            private int outstanding = 1;

            internal PendingEntry(IDictionary dictionary)
            {
                this.dictionary = dictionary;
            }

            /// <summary>
            ///     Counts one more half to wait for.
            /// </summary>
            internal void Await()
            {
                Interlocked.Increment(ref this.outstanding);
            }

            internal void ResolveKey(object target, object entity)
            {
                this.key = entity;
                this.Release();
            }

            internal void ResolveValue(object target, object entity)
            {
                this.value = entity;
                this.Release();
            }

            /// <summary>
            ///     Both halves are read: takes the ones that were not entities, and releases
            ///     the count held for the reading.
            /// </summary>
            internal void Read(bool hasKey, object readKey, bool hasValue, object readValue)
            {
                if (hasKey)
                {
                    this.key = readKey;
                }

                if (hasValue)
                {
                    this.value = readValue;
                }

                this.Release();
            }

            private void Release()
            {
                if (Interlocked.Decrement(ref this.outstanding) != 0 || this.key == null)
                {
                    return;
                }

                lock (this.dictionary)
                {
                    this.dictionary.Add(this.key, this.value);
                }
            }
        }
    }
}
