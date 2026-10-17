namespace Hyperlite
{
    using System;

    /// <summary>
    /// Specifies the table mutator flags.
    /// </summary>
    /// <seealso cref="MutatorSpec" />
    [Serializable]
    [Flags]
    public enum MutatorFlags
    {
        /// <summary>
        /// No flags.
        /// </summary>
        Default = 0x00,

        /// <summary>
        /// Hypertable's commit log sync; the SQLite store has no commit log, so this changes
        /// nothing.
        /// </summary>
        NoLogSync = 0x01,

        /// <summary>
        /// Hypertable's skipping of unknown column families; the SQLite store does not skip
        /// them, a cell in one is refused with a <see cref="BadKeyException" />.
        /// </summary>
        IgnoreUnknownCf = 0x02
    }
}
