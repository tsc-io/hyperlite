namespace Hyperlite.Persistence.Serialization
{
    using Hyperlite.Persistence.Reflection;

    internal sealed class TypeSchemaProperty
    {
        public EncoderInfo EncoderInfo;

        public InspectedProperty InspectedProperty;

        public string PropertyName;
    }
}
