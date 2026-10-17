namespace Hyperlite.Persistence.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using Hyperlite.Persistence.Reflection;

    internal sealed class TypeSchema
    {
        public const byte Version = 0;

        private readonly Lazy<byte[]> serializedSchema;

        private readonly TypeSchemaProperty[] typeSchemaProperties;

        private readonly Action<Serializer, object> writeObject;

        internal TypeSchema(TypeSchemaProperty[] typeSchemaProperties)
        {
            this.typeSchemaProperties = typeSchemaProperties;
            this.serializedSchema = new Lazy<byte[]>(this.GetSerializedSchema);
        }

        internal TypeSchema(Inspector inspector, bool handlePropertyIgnore)
        {
            var properties = new List<TypeSchemaProperty>(inspector.Properties.Count);
            foreach (var property in inspector.Properties.Where(property => !handlePropertyIgnore || !property.Ignore))
            {
                var typeSchemaProperty = new TypeSchemaProperty();
                typeSchemaProperty.PropertyName = property.Name;
                typeSchemaProperty.InspectedProperty = property;
                Encoder.TryGetEncoder(property.PropertyType, out typeSchemaProperty.EncoderInfo);
                properties.Add(typeSchemaProperty);
            }

            this.typeSchemaProperties = properties.ToArray();
            this.writeObject = TypeSchema.CompileWriteObjectDelegate(this.typeSchemaProperties);
            this.serializedSchema = new Lazy<byte[]>(this.GetSerializedSchema);
        }

        internal TypeSchemaProperty[] Properties => this.typeSchemaProperties;

        internal byte[] SerializedSchema => this.serializedSchema.Value;

        internal Action<Serializer, object> WriteObject => this.writeObject;

        private static Action<Serializer, object> CompileWriteObjectDelegate(
            ICollection<TypeSchemaProperty> typeSchemaProperties)
        {
            if (typeSchemaProperties.Count > 0)
            {
                var serializer = Expression.Parameter(typeof(Serializer), @"serializer");
                var any = Expression.Parameter(typeof(object), @"any");

                var writeProperty =
                    typeof(TypeSchema).GetMethod(nameof(WriteProperty), BindingFlags.Static | BindingFlags.NonPublic);
                var encodeProperty =
                    typeof(TypeSchema).GetMethod(nameof(EncodeProperty), BindingFlags.Static | BindingFlags.NonPublic);

                var expressions = new List<Expression>(typeSchemaProperties.Count);
                expressions.AddRange(
                    typeSchemaProperties.Select(
                        typeSchemaProperty =>
                            typeSchemaProperty.EncoderInfo == null
                                ? Expression.Call(null, writeProperty, serializer,
                                    Expression.Constant(typeSchemaProperty.InspectedProperty), any)
                                : Expression.Call(
                                    null,
                                    encodeProperty,
                                    serializer,
                                    Expression.Constant(typeSchemaProperty.InspectedProperty),
                                    Expression.Constant(typeSchemaProperty.EncoderInfo),
                                    any)));

                var block = Expression.Block(expressions);
                return Expression.Lambda<Action<Serializer, object>>(block, serializer, any).Compile();
            }

            return (serializer, o) => { };
        }

        /// <param name="any">
        ///     The object to which the property belongs.
        /// </param>
        private static void EncodeProperty(Serializer serializer, InspectedProperty inspectedProperty,
            EncoderInfo encoderInfo, object any)
        {
            var value = inspectedProperty.Getter(any);
            if (value == null)
            {
                Encoder.WriteTag(serializer.BinaryWriter, Tags.Null);
            }
            else
            {
                serializer.Encode(encoderInfo, value, true);
            }
        }

        /// <param name="any">
        ///     The object to which the property belongs.
        /// </param>
        private static void WriteProperty(Serializer serializer, InspectedProperty inspectedProperty, object any)
        {
            var value = inspectedProperty.Getter(any);
            if (value == null)
            {
                Encoder.WriteTag(serializer.BinaryWriter, Tags.Null);
            }
            else
            {
                serializer.Write(inspectedProperty.PropertyType, value.GetType(), value);
            }
        }

        // UTF-8, as the reader decodes it and every other writer writes: for an ascii name the
        // bytes are the same either way, and a non-ascii one used to be written as '?' and lost
        private byte[] GetSerializedSchema()
        {
            using (var binaryWriter = new HeapBinaryWriter())
            {
                Encoder.WriteTag(binaryWriter, Tags.TypeSchema2);
                Encoder.WriteByte(binaryWriter, TypeSchema.Version, false);
                Encoder.WriteCount(binaryWriter, this.Properties.Length);
                foreach (var typeSchemaProperty in this.Properties)
                {
                    Encoder.WriteString(binaryWriter, typeSchemaProperty.PropertyName, false);
                }

                return binaryWriter.ToArray();
            }
        }
    }
}
