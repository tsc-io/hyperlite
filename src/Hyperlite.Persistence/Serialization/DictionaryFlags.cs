namespace Hyperlite.Persistence.Serialization
{
    using System;

    [Flags]
    public enum DictionaryFlags
    {
        None = 0,

        /// <summary>
        ///     Set if a typed dictionary.
        /// </summary>
        Typed = 1,

        /// <summary>
        ///     Set if a tag for the declared key type have been written.
        /// </summary>
        KeyTypeTagged = 1 << 1,

        /// <summary>
        ///     Set if a tag for the declared value type have been written.
        /// </summary>
        ValueTypeTagged = 1 << 2,

        /// <summary>
        ///     Set if the key values have been tagged.
        /// </summary>
        KeyValueTagged = 1 << 3,

        /// <summary>
        ///     Set if the values have been tagged.
        /// </summary>
        ValueTagged = 1 << 4
    }
}
