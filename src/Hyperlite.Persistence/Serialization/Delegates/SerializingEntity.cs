namespace Hyperlite.Persistence.Serialization.Delegates
{
    using System;

    /// <param name="isRoot">
    ///     Indicating whether this object is the root of the object graph.
    /// </param>
    /// <returns>
    ///     The entity key.
    /// </returns>
    internal delegate Key SerializingEntity(bool isRoot, EntityReference entityReference, Type serializeType,
        object value);
}
