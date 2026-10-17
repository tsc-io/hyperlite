namespace Hyperlite
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents a database context, the root of everything else.
    /// </summary>
    /// <remarks>
    /// A context opens the connection to the provider. Create one with
    /// <see cref="Context.Create(string)" />, get an <see cref="IClient" /> from it, and
    /// dispose it when done, last: a client, namespace, table, mutator or scanner obtained
    /// through it keeps the connection open until it is disposed itself. Contexts on one
    /// database file share that connection, opened with the settings of the first; a context
    /// created after the last one was disposed, while such a handle still holds the old
    /// connection, opens a second one beside it.
    /// </remarks>
    public interface IContext : IDisposable
    {
        /// <summary>
        /// Gets the resolved configuration properties, including the defaults that were
        /// applied.
        /// </summary>
        IDictionary<string, object> Properties { get; }

        /// <summary>
        /// Gets a value indicating whether this context has been disposed.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Creates a client on this context.
        /// </summary>
        /// <returns>A new client. Dispose it when done.</returns>
        IClient CreateClient();
    }
}
