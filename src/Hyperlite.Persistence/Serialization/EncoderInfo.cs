namespace Hyperlite.Persistence.Serialization
{
    using System;
    using Hyperlite.Persistence.Extensions;
    using Hyperlite.Persistence.Serialization.Delegates;

    internal sealed class EncoderInfo
    {
        internal readonly Tags Tag;

        private readonly Encode encode;

        /// <summary>
        ///     The encode delegate for encoders that need the serializer.
        /// </summary>
        private readonly EncodeWithSerializer encodeWithSerializer;

        private readonly Serialize serialize;

        /// <param name="encode">
        ///     The encode delegate.
        /// </param>
        internal EncoderInfo(Tags tag, Encode encode)
        {
            this.Tag = tag;
            this.encode = encode;
            this.encodeWithSerializer = null;
            this.serialize = null;
        }

        /// <param name="encodeWithSerializer">
        ///     The encode delegate for encoders that need the serializer.
        /// </param>
        internal EncoderInfo(Tags tag, EncodeWithSerializer encodeWithSerializer)
        {
            this.Tag = tag;
            this.encode = null;
            this.encodeWithSerializer = encodeWithSerializer;
            this.serialize = null;
        }

        /// <param name="serialize">
        ///     The serialize delegate.
        /// </param>
        internal EncoderInfo(Tags tag, Serialize serialize)
        {
            this.Tag = tag;
            this.encode = null;
            this.encodeWithSerializer = null;
            this.serialize = serialize;
        }

        /// <param name="any">
        ///     The object to encode.
        /// </param>
        /// <param name="writeTag">
        ///     If <c>true</c> the encoder must writes the leading tag, otherwise <c>false</c>.
        /// </param>
        internal void Encode(Serializer serializer, object any, bool writeTag)
        {
            if (this.encode != null)
            {
                this.encode(serializer.BinaryWriter, any, writeTag);
            }
            else if (this.encodeWithSerializer != null)
            {
                this.encodeWithSerializer(serializer, any, writeTag);
            }
            else
            {
                if (writeTag)
                {
                    Encoder.WriteTag(serializer.BinaryWriter, this.Tag);
                }

                this.serialize(serializer, any);
            }
        }

        /// <summary>
        ///     Check if the value type requires object ref handling.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the type requires ObjectRef handling; otherwise <c>false</c>.
        /// </returns>
        internal bool HandleObjectRef(Type type)
        {
            ////TODO review, correct?
            return this.Tag >= Tags.FirstCustomType || (this.Tag >= Tags.TypeCode && type.IsComplex());
        }
    }
}
