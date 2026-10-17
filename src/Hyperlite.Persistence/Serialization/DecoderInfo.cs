namespace Hyperlite.Persistence.Serialization
{
    using System;
    using Hyperlite.Persistence.Serialization.Delegates;

    internal sealed class DecoderInfo
    {
        internal readonly Deserialize Deserialize;

        /// <summary>
        ///     The type to deserialize.
        /// </summary>
        internal readonly Type Type;

        internal DecoderInfo(Type type, Deserialize deserialize)
        {
            this.Type = type;
            this.Deserialize = deserialize;
        }
    }
}
