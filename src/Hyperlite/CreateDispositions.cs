namespace Hyperlite
{
    using System;

    /// <summary>
    /// Specifies the create dispositions.
    /// </summary>
    /// <seealso cref="IClient" />
    [Serializable]
    [Flags]
    public enum CreateDispositions
    {
        /// <summary>
        /// No disposition.
        /// </summary>
        None = 0,

        /// <summary>
        /// Create all intermediate namespaces.
        /// </summary>
        CreateIntermediate = 1,

        /// <summary>
        /// Do not fail if the namespace already exists.
        /// </summary>
        CreateIfNotExist = 2
    }
}
