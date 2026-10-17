namespace Hyperlite
{
    using System;

    /// <summary>
    /// Specifies match kinds.
    /// </summary>
    /// <remarks>
    /// Match kind is used on column predicates.
    /// </remarks>
    /// <seealso cref="ColumnPredicate" />
    [Serializable]
    [Flags]
    public enum MatchKind
    {
        Undefined = 0,

        /// <summary>
        /// Exact value match for the search.
        /// </summary>
        ValueExact = 0x0001,

        /// <summary>
        /// Value prefix match for the search.
        /// </summary>
        ValuePrefix = 0x0002,

        /// <summary>
        /// Value regular expression match for the search.
        /// </summary>
        ValueRegex = 0x0004,

        /// <summary>
        /// Exact qualifier match for the search.
        /// </summary>
        /// <remarks>Can be combined with ValueExact.</remarks>
        QualifierExact = 0x0100,

        /// <summary>
        /// Qualifier prefix match for the search.
        /// </summary>
        /// <remarks>Can be combined with ValueExact.</remarks>
        QualifierPrefix = 0x0200,

        /// <summary>
        /// Qualifier regular expression match for the search.
        /// </summary>
        /// <remarks>Can be combined with ValueExact.</remarks>
        QualifierRegex = 0x0400
    }
}
