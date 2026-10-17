namespace Hyperlite
{
    using System;

    /// <summary>
    /// Specifies the table scanner flags.
    /// </summary>
    /// <seealso cref="ScanSpec" />
    [Serializable]
    [Flags]
    public enum ScannerFlags
    {
        /// <summary>
        /// No flags.
        /// </summary>
        Default = 0x00,

        /// <summary>
        /// Hypertable's table cache; the SQLite store has none, so this changes nothing.
        /// </summary>
        BypassTableCache = 0x01,

        /// <summary>
        /// Hypertable's table cache; the SQLite store has none, so this changes nothing.
        /// </summary>
        RefreshTableCache = 0x02,

        /// <summary>
        /// Hypertable's table cache; the SQLite store has none, so this changes nothing.
        /// </summary>
        NoAutoTableRefresh = 0x04
    }
}
