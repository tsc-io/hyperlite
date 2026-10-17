namespace Hyperlite
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents a table mutator, writes cells to a table.
    /// </summary>
    /// <remarks>
    /// Disposing a mutator flushes whatever it still holds.
    ///
    /// <see cref="TableMutator" /> and <see cref="ChunkedTableMutator" /> are safe to
    /// share between threads: every entry point takes a lock, and the chunked mutator's
    /// buffer is guarded by one of its own. The entity manager relies on that - it keeps
    /// one mutator per table for all its threads - and a test pins it for both. What is
    /// not ordered is the interleaving: cells written from two threads reach the table in
    /// whichever order their calls took the lock. Other implementations need not be safe
    /// to share, and <see cref="QueuedTableMutator" /> is not covered by this.
    /// </remarks>
    public interface ITableMutator : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether this mutator has been disposed.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Writes a cell.
        /// </summary>
        /// <param name="key">Cell key, mandatory. An empty row is filled in with a
        /// generated key and written back to the instance.</param>
        /// <param name="value">Cell value, might be null.</param>
        void Set(Key key, byte[] value);

        /// <summary>
        /// Writes a cell.
        /// </summary>
        /// <param name="key">Cell key, mandatory.</param>
        /// <param name="value">Cell value, might be null.</param>
        /// <param name="createRowKey">Generate the row key even where the key carries one.</param>
        void Set(Key key, byte[] value, bool createRowKey);

        /// <summary>
        /// Writes a cell whose value lives in unmanaged memory.
        /// </summary>
        /// <param name="key">Cell key, mandatory.</param>
        /// <param name="value">Pointer to the value, or <see cref="IntPtr.Zero" /> where the length is 0.</param>
        /// <param name="valueLength">Value length, in bytes.</param>
        void Set(Key key, IntPtr value, int valueLength);

        /// <summary>
        /// Writes a cell.
        /// </summary>
        /// <param name="cell">Cell, mandatory.</param>
        void Set(Cell cell);

        /// <summary>
        /// Writes a cell.
        /// </summary>
        /// <param name="cell">Cell, mandatory.</param>
        /// <param name="createRowKey">Generate the row key even where the cell carries one.</param>
        void Set(Cell cell, bool createRowKey);

        /// <summary>
        /// Writes a batch of cells, refused whole when one of them is: none is taken, and none is
        /// given a row key.
        /// </summary>
        /// <param name="cells">Cells, mandatory.</param>
        void Set(IEnumerable<Cell> cells);

        /// <summary>
        /// Writes a batch of cells, refused whole when one of them is: none is taken, and none is
        /// given a row key.
        /// </summary>
        /// <param name="cells">Cells, mandatory.</param>
        /// <param name="createRowKey">Generate the row keys even where the cells carry them.</param>
        void Set(IEnumerable<Cell> cells, bool createRowKey);

        /// <summary>
        /// Deletes a row: its cells at or older than now, which leaves those given a timestamp
        /// still to come.
        /// </summary>
        /// <param name="row">Row key, mandatory.</param>
        void Delete(string row);

        /// <summary>
        /// Deletes a row, a column family within a row, or a single cell, depending on how
        /// much the key specifies: the cells at or older than the key's timestamp, 0 meaning now.
        /// </summary>
        /// <param name="key">Key, mandatory.</param>
        void Delete(Key key);

        /// <summary>
        /// Deletes what each key specifies, as <see cref="Delete(Key)" /> does - one key at
        /// a time, unlike a batch set: a key refused part way leaves the deletes before it
        /// done.
        /// </summary>
        /// <param name="keys">Keys, mandatory.</param>
        void Delete(IEnumerable<Key> keys);

        /// <summary>
        /// Deletes what each cell's key specifies, as <see cref="Delete(Key)" /> does: a cell
        /// read by a scan carries its timestamp, so its older versions go with it. One cell at
        /// a time, as for keys.
        /// </summary>
        /// <param name="cells">Cells, mandatory.</param>
        void Delete(IEnumerable<Cell> cells);

        /// <summary>
        /// Flushes everything accumulated so far.
        /// </summary>
        void Flush();
    }
}
