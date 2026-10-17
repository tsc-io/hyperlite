namespace Hyperlite.Persistence.Serialization.Delegates
{
    using System;

    internal delegate void DeserializingEntity(EntityReference entityReference, Type destinationType, object value);
}
