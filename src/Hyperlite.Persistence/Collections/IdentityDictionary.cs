namespace Hyperlite.Persistence.Collections
{
    /// <summary>
    ///     A flat table: nothing enumerates it, so its slot order cannot leak anywhere.
    /// </summary>
    /// <typeparam name="T">
    ///     The value type.
    /// </typeparam>
    internal sealed class IdentityDictionary<T> : FlatDictionary<object, T, IdentityComparer>
    {
        internal IdentityDictionary(int initialBucketCount)
            : base(initialBucketCount)
        {
        }
    }
}
