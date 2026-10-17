namespace Hyperlite.Persistence.Attributes
{
    using System;

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class TransientAttribute : Attribute
    {
    }
}
