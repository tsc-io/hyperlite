namespace Hyperlite
{
    using System;

    /// <summary>
    /// Specifies the context kinds, one per provider.
    /// </summary>
    /// <seealso cref="Context" />
    [Serializable]
    public enum ContextKind
    {
        /// <summary>
        /// Unknown provider.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// SQLite provider.
        /// </summary>
        SQLite = 3,
    }
}
