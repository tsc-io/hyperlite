namespace Hyperlite.Persistence.Serialization
{
    using System;

    [Flags]
    internal enum ArrayFlags
    {
        None = 0,

        /// <summary>
        ///     The array rank mask.
        /// </summary>
        RankMask = 0x2f,

        /// <summary>
        ///     Set if the element values have not been tagged.
        /// </summary>
        ValueNotTagged = 0x40,

        /// <summary>
        ///     Set if the element values have been tagged.
        /// </summary>
        ValueTagged = 0x80
    }
}
