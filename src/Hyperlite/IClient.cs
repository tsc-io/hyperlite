namespace Hyperlite
{
    using System;

    /// <summary>
    /// Represents a client, provides methods to handle namespaces.
    /// </summary>
    public interface IClient : IDisposable
    {
        /// <summary>
        /// Gets the context this client was created on.
        /// </summary>
        IContext Context { get; }

        /// <summary>
        /// Gets a value indicating whether this client has been disposed.
        /// </summary>
        bool IsDisposed { get; }

        /// <param name="name">Namespace name, mandatory.</param>
        void CreateNamespace(string name);

        /// <param name="name">Namespace name, mandatory.</param>
        /// <param name="dispo">Create disposition.</param>
        void CreateNamespace(string name, CreateDispositions dispo);

        /// <summary>
        /// Creates a namespace below an existing one.
        /// </summary>
        /// <param name="name">Namespace name, mandatory.</param>
        /// <param name="nsBase">Base namespace, or null for the root.</param>
        void CreateNamespace(string name, INamespace nsBase);

        /// <summary>
        /// Creates a namespace below an existing one.
        /// </summary>
        /// <param name="name">Namespace name, mandatory.</param>
        /// <param name="nsBase">Base namespace, or null for the root.</param>
        /// <param name="dispo">Create disposition.</param>
        void CreateNamespace(string name, INamespace nsBase, CreateDispositions dispo);

        /// <param name="name">Namespace name.</param>
        /// <returns>The namespace. Dispose it when done.</returns>
        INamespace OpenNamespace(string name);

        /// <param name="name">Namespace name.</param>
        /// <param name="dispo">Open disposition.</param>
        /// <returns>The namespace. Dispose it when done.</returns>
        INamespace OpenNamespace(string name, OpenDispositions dispo);

        /// <summary>
        /// Opens a namespace below an existing one.
        /// </summary>
        /// <param name="name">Namespace name.</param>
        /// <param name="nsBase">Base namespace, or null for the root.</param>
        /// <returns>The namespace. Dispose it when done.</returns>
        INamespace OpenNamespace(string name, INamespace nsBase);

        /// <summary>
        /// Opens a namespace below an existing one.
        /// </summary>
        /// <param name="name">Namespace name.</param>
        /// <param name="nsBase">Base namespace, or null for the root.</param>
        /// <param name="dispo">Open disposition.</param>
        /// <returns>The namespace. Dispose it when done.</returns>
        INamespace OpenNamespace(string name, INamespace nsBase, OpenDispositions dispo);

        /// <param name="name">Namespace name, mandatory.</param>
        void DropNamespace(string name);

        /// <param name="name">Namespace name, mandatory.</param>
        /// <param name="dispo">Drop disposition.</param>
        void DropNamespace(string name, DropDispositions dispo);

        /// <summary>
        /// Drops a namespace below an existing one.
        /// </summary>
        /// <param name="name">Namespace name, mandatory.</param>
        /// <param name="nsBase">Base namespace, or null for the root.</param>
        void DropNamespace(string name, INamespace nsBase);

        /// <summary>
        /// Drops a namespace below an existing one.
        /// </summary>
        /// <param name="name">Namespace name, mandatory.</param>
        /// <param name="nsBase">Base namespace, or null for the root.</param>
        /// <param name="dispo">Drop disposition.</param>
        void DropNamespace(string name, INamespace nsBase, DropDispositions dispo);

        /// <summary>
        /// Determines whether a namespace exists.
        /// </summary>
        /// <param name="name">Namespace name, mandatory.</param>
        /// <returns>true if it exists, otherwise false.</returns>
        bool NamespaceExists(string name);

        /// <summary>
        /// Determines whether a namespace below an existing one exists.
        /// </summary>
        /// <param name="name">Namespace name, mandatory.</param>
        /// <param name="nsBase">Base namespace, or null for the root.</param>
        /// <returns>true if it exists, otherwise false.</returns>
        bool NamespaceExists(string name, INamespace nsBase);

        /// <summary>
        /// Compacts the database, reclaiming unused space.
        /// </summary>
        void Optimize();
    }
}
