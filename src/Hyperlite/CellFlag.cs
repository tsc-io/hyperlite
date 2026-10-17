namespace Hyperlite
{
    using System;

    /// <summary>
    /// Specifies the cell flags.
    /// </summary>
    /// <remarks>
    /// Cell flags are used on <see cref="ITableMutator" /> operations. The values reach
    /// the database, so they are fixed. A delete takes the cells at or older than the cell's
    /// timestamp, 0 meaning now; a cell version delete takes the one at exactly that
    /// timestamp, so it needs one given.
    /// </remarks>
    /// <seealso cref="Cell" />
    [Serializable]
    public enum CellFlag
    {
        /// <summary>
        /// Deletes an entire row.
        /// </summary>
        DeleteRow = 0x00,

        /// <summary>
        /// Deletes a column family in a particular row.
        /// </summary>
        DeleteColumnFamily = 0x01,

        /// <summary>
        /// Deletes a specific cell within a row.
        /// </summary>
        DeleteCell = 0x02,

        /// <summary>
        /// Deletes a specific cell version within a row.
        /// </summary>
        DeleteCellVersion = 0x03,

        /// <summary>
        /// Inserts a cell.
        /// </summary>
        Default = 0xFF
    }
}
