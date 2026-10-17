namespace Hyperlite
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents a table scanner, reads cells from a table.
    /// </summary>
    /// <remarks>
    /// A scanner is forward only and single pass. Enumerating it consumes it; there is no
    /// reset.
    ///
    /// The Move overloads fill a cell the caller owns, which avoids an allocation per
    /// cell - use <see cref="BufferedCell" /> or <see cref="PooledCell" /> to avoid the
    /// value buffer allocation too. <see cref="Next(out Cell)" /> and the enumerator
    /// allocate a fresh cell each time.
    /// </remarks>
    public interface ITableScanner : IEnumerable<Cell>, IDisposable
    {
        /// <summary>
        /// Gets the scan specification this scanner was created with, or null.
        /// </summary>
        ScanSpec ScanSpec { get; }

        /// <summary>
        /// Gets a value indicating whether this scanner has been disposed.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Reads the next cell into the given instance.
        /// </summary>
        /// <param name="cell">Cell to fill, mandatory.</param>
        /// <returns>false once the scan is exhausted.</returns>
        bool Move(Cell cell);

        /// <summary>
        /// Reads the next cell into the given instance, reusing its value buffer.
        /// </summary>
        /// <param name="cell">Cell to fill, mandatory.</param>
        /// <returns>false once the scan is exhausted.</returns>
        bool Move(BufferedCell cell);

        /// <summary>
        /// Reads the next cell into the given instance, renting its value buffer from a pool.
        /// </summary>
        /// <param name="cell">Cell to fill, mandatory.</param>
        /// <returns>false once the scan is exhausted.</returns>
        bool Move(PooledCell cell);

        /// <summary>
        /// Reads the next cell into a fresh instance.
        /// </summary>
        /// <param name="cell">Receives the cell, or null once exhausted.</param>
        /// <returns>false once the scan is exhausted.</returns>
        bool Next(out Cell cell);

        /// <summary>
        /// Reads the next cell and hands its key and raw value to the given function,
        /// without copying the value.
        /// </summary>
        /// <param name="action">Receives the key, a pointer to the value and its length.
        /// The pointer is only valid for the duration of the call.</param>
        /// <returns>What the function returned, or false once the scan is exhausted.</returns>
        bool Next(Func<Key, IntPtr, int, bool> action);
    }
}
