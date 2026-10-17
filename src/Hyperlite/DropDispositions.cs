namespace Hyperlite
{
    using System;

    /// <summary>
    /// Specifies the drop dispositions.
    /// </summary>
    /// <seealso cref="IClient" />
    /// <seealso cref="INamespace" />
    [Serializable]
    [Flags]
    public enum DropDispositions
    {
        /// <summary>
        /// No disposition.
        /// </summary>
        None = 0,

        /// <summary>
        /// Do not fail if the namespace or table does not exist.
        /// </summary>
        IfExists = 1,

        /// <summary>
        /// Drop the tables of the namespace as well.
        /// </summary>
        IncludeTables = 1 << 1,

        /// <summary>
        /// Drop the entire sub-tree.
        /// </summary>
        Deep = 1 << 2,

        /// <summary>
        /// IfExists, IncludeTables and Deep combined.
        /// </summary>
        Complete = IfExists | IncludeTables | Deep
    }
}
