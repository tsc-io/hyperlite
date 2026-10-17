namespace Hyperlite
{
    using System;

    /// <summary>
    /// Specifies the table mutator kinds.
    /// </summary>
    /// <seealso cref="MutatorSpec" />
    [Serializable]
    public enum MutatorKind
    {
        /// <summary>
        /// Regular table mutator.
        /// </summary>
        Default,

        /// <summary>
        /// Chunked table mutator, accumulates cells and flushes them in chunks.
        /// </summary>
        Chunked
    }
}
