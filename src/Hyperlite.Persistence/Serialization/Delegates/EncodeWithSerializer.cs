namespace Hyperlite.Persistence.Serialization.Delegates
{
    /// <summary>
    ///     The encode delegate for encoders that need the serializer.
    /// </summary>
    /// <param name="any">
    ///     The object to encode.
    /// </param>
    /// <param name="writeTag">
    ///     If <c>true</c> the encoder writes the leading type tag, otherwise <c>false</c>.
    /// </param>
    /// <remarks>
    ///     <see cref="Encode" /> gets only the writer, so an encoder that also needs the
    ///     serializer's configuration had to be a bound instance method - which meant a
    ///     delegate and an <see cref="EncoderInfo" /> allocated per serializer, and a
    ///     serializer is constructed per serialized value. This shape takes the serializer
    ///     as an argument instead, so such an encoder can be a static method and its
    ///     <see cref="EncoderInfo" /> a shared static instance.
    /// </remarks>
    internal delegate void EncodeWithSerializer(Serializer serializer, object any, bool writeTag);
}
