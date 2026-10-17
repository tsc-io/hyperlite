namespace Hyperlite.Persistence.Serialization
{
    using System;

    [Flags]
    public enum CollectionFlags
    {
        None = 0,

        /// <summary>
        ///     Set if a typed collection.
        /// </summary>
        Typed = 1,

        /// <summary>
        ///     Set if a tag for the declared element type have been written.
        /// </summary>
        TypeTagged = 1 << 1,

        Set = 1 << 2,

        /// <summary>
        ///     Set if the element values have been tagged.
        /// </summary>
        ValueTagged = 1 << 3
    }
}
