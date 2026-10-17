namespace Hyperlite
{
    using System;

    /// <summary>
    /// Represents a table, provides methods to read and write cells.
    /// </summary>
    public interface ITable : IDisposable
    {
        /// <summary>
        /// Gets the table name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the table's xml schema, without ids.
        /// </summary>
        string Schema { get; }

        /// <summary>
        /// Gets a value indicating whether this table has been disposed.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Creates a mutator on this table.
        /// </summary>
        /// <returns>The mutator. Dispose it to flush and release it.</returns>
        ITableMutator CreateMutator();

        /// <summary>
        /// Creates a mutator on this table.
        /// </summary>
        /// <param name="mutatorSpec">Mutator specification, or null for the default.</param>
        /// <returns>The mutator. Dispose it to flush and release it.</returns>
        ITableMutator CreateMutator(MutatorSpec mutatorSpec);

        /// <summary>
        /// Creates a scanner over the whole table.
        /// </summary>
        /// <returns>The scanner. Dispose it when done.</returns>
        ITableScanner CreateScanner();

        /// <param name="scanSpec">Scan specification, or null for the whole table.</param>
        /// <returns>The scanner. Dispose it when done.</returns>
        ITableScanner CreateScanner(ScanSpec scanSpec);

        /// <summary>
        /// Gets the table's schema as an object model, including the ids.
        /// </summary>
        /// <returns>The parsed schema.</returns>
        Xml.TableSchema GetTableSchema();
    }
}
