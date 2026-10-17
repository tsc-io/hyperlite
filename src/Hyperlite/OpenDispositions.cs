namespace Hyperlite
{
    using System;

    /// <summary>
    /// Specifies the open dispositions.
    /// </summary>
    /// <seealso cref="IClient" />
    /// <seealso cref="INamespace" />
    [Serializable]
    [Flags]
    public enum OpenDispositions
    {
        /// <summary>
        /// Open an existing namespace or table, fail if it does not exist.
        /// </summary>
        OpenExisting = 0x00,

        /// <summary>
        /// Open the namespace or table, create it if it does not exist.
        /// </summary>
        OpenAlways = 0x01,

        /// <summary>
        /// Always create the namespace or table.
        /// </summary>
        CreateAlways = 0x02,

        /// <summary>
        /// Create all intermediate namespaces.
        /// </summary>
        CreateIntermediate = 0x10,

        /// <summary>
        /// Force the operation.
        /// </summary>
        Force = 0x20
    }
}
