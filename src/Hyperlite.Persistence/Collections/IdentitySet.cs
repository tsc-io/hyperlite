namespace Hyperlite.Persistence.Collections
{
    /// <summary>
    ///     A flat table. EntityWriter only adds to it and asks it, so its slot order cannot
    ///     leak anywhere.
    /// </summary>
    internal sealed class IdentitySet : FlatHashSet<object, IdentityComparer>
    {
        internal IdentitySet()
        {
        }
    }
}
