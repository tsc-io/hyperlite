namespace Hyperlite.Persistence.Serialization.Delegates
{
    using System.IO;

    /// <param name="any">
    ///     The object to encode.
    /// </param>
    /// <param name="writeTag">
    ///     If <c>true</c> the encoder writes the leading type tag, otherwise <c>false</c>.
    /// </param>
    internal delegate void Encode(BinaryWriter binaryWriter, object any, bool writeTag);
}
