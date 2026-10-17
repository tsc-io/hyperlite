namespace Hyperlite
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Represents a namespace, provides methods to handle tables and nested
    /// namespaces.
    /// </summary>
    public interface INamespace : IDisposable
    {
        /// <summary>
        /// Gets the full name of this namespace.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the names of the direct sub-namespaces.
        /// </summary>
        IList<string> Namespaces { get; }

        /// <summary>
        /// Gets the names of the tables in this namespace.
        /// </summary>
        IList<string> Tables { get; }

        /// <summary>
        /// Gets a value indicating whether this namespace has been disposed.
        /// </summary>
        bool IsDisposed { get; }

        /// <param name="name">Table name, mandatory.</param>
        /// <param name="schema">Xml table schema, mandatory.</param>
        void CreateTable(string name, string schema);

        /// <param name="name">Table name, mandatory.</param>
        /// <param name="schema">Xml table schema, mandatory.</param>
        /// <param name="dispo">Create disposition.</param>
        void CreateTable(string name, string schema, CreateDispositions dispo);

        /// <summary>
        /// Creates a table with the schema of an existing one.
        /// </summary>
        /// <param name="name">Table name, mandatory.</param>
        /// <param name="like">Name of the table to copy the schema from, mandatory.</param>
        void CreateTableLike(string name, string like);

        /// <summary>
        /// Creates a table with the schema of an existing one.
        /// </summary>
        /// <param name="name">Table name, mandatory.</param>
        /// <param name="like">Name of the table to copy the schema from, mandatory.</param>
        /// <param name="dispo">Create disposition.</param>
        void CreateTableLike(string name, string like, CreateDispositions dispo);

        /// <summary>
        /// Alters a table's schema. Existing column families keep their ids and their cells; a
        /// column family the new schema no longer names is removed with its cells - renaming
        /// one removes it and adds another. A family changing its time order keeps its
        /// timestamps, and the table keeps its options. A mutator open on the table follows the
        /// new schema, a scanner open on it that has not ended fails its next call with a
        /// <see cref="RangeServerException"/>.
        /// </summary>
        /// <param name="name">Table name, mandatory.</param>
        /// <param name="schema">Xml table schema, mandatory.</param>
        void AlterTable(string name, string schema);

        /// <param name="nameOld">Existing table name, mandatory.</param>
        /// <param name="nameNew">New table name, mandatory.</param>
        void RenameTable(string nameOld, string nameNew);

        /// <param name="name">Table name, mandatory.</param>
        /// <returns>The table. Dispose it when done.</returns>
        ITable OpenTable(string name);

        /// <param name="name">Table name, mandatory.</param>
        /// <param name="dispo">Open disposition.</param>
        /// <returns>The table. Dispose it when done.</returns>
        ITable OpenTable(string name, OpenDispositions dispo);

        /// <summary>
        /// Opens a table, creating it from the given schema where the disposition asks for it.
        /// </summary>
        /// <param name="name">Table name, mandatory.</param>
        /// <param name="schema">Xml table schema, mandatory.</param>
        /// <param name="dispo">Open disposition.</param>
        /// <returns>The table. Dispose it when done.</returns>
        ITable OpenTable(string name, string schema, OpenDispositions dispo);

        /// <param name="name">Table name, mandatory.</param>
        void DropTable(string name);

        /// <param name="name">Table name, mandatory.</param>
        /// <param name="dispo">Drop disposition.</param>
        void DropTable(string name, DropDispositions dispo);

        /// <summary>
        /// Drops every table in this namespace.
        /// </summary>
        void DropTables();

        /// <summary>
        /// Drops every table in this namespace whose name matches.
        /// </summary>
        /// <param name="regex">Table name expression, mandatory.</param>
        /// <param name="dispo">Drop disposition.</param>
        void DropTables(Regex regex, DropDispositions dispo);

        /// <summary>
        /// Determines whether a table exists.
        /// </summary>
        /// <param name="name">Table name, mandatory.</param>
        /// <returns>true if it exists, otherwise false.</returns>
        bool TableExists(string name);

        /// <summary>
        /// Gets a table's xml schema.
        /// </summary>
        /// <param name="name">Table name, mandatory.</param>
        /// <returns>The xml schema.</returns>
        string GetTableSchema(string name);

        /// <summary>
        /// Gets a table's xml schema.
        /// </summary>
        /// <param name="name">Table name, mandatory.</param>
        /// <param name="withIds">Include the generation and the column family ids.</param>
        /// <returns>The xml schema.</returns>
        string GetTableSchema(string name, bool withIds);

        /// <summary>
        /// Creates a sub-namespace.
        /// </summary>
        /// <param name="name">Namespace name, mandatory.</param>
        void CreateNamespace(string name);

        /// <summary>
        /// Creates a sub-namespace.
        /// </summary>
        /// <param name="name">Namespace name, mandatory.</param>
        /// <param name="dispo">Create disposition.</param>
        void CreateNamespace(string name, CreateDispositions dispo);

        /// <summary>
        /// Opens a sub-namespace.
        /// </summary>
        /// <param name="name">Namespace name.</param>
        /// <returns>The namespace. Dispose it when done.</returns>
        INamespace OpenNamespace(string name);

        /// <summary>
        /// Opens a sub-namespace.
        /// </summary>
        /// <param name="name">Namespace name.</param>
        /// <param name="dispo">Open disposition.</param>
        /// <returns>The namespace. Dispose it when done.</returns>
        INamespace OpenNamespace(string name, OpenDispositions dispo);

        /// <summary>
        /// Drops a sub-namespace.
        /// </summary>
        /// <param name="name">Namespace name, mandatory.</param>
        void DropNamespace(string name);

        /// <summary>
        /// Drops a sub-namespace.
        /// </summary>
        /// <param name="name">Namespace name, mandatory.</param>
        /// <param name="dispo">Drop disposition.</param>
        void DropNamespace(string name, DropDispositions dispo);

        /// <summary>
        /// Drops every sub-namespace.
        /// </summary>
        /// <param name="dispo">Drop disposition.</param>
        void DropNamespaces(DropDispositions dispo);

        /// <summary>
        /// Drops every sub-namespace whose name matches.
        /// </summary>
        /// <param name="regex">Namespace name expression, mandatory.</param>
        /// <param name="dispo">Drop disposition.</param>
        void DropNamespaces(Regex regex, DropDispositions dispo);

        /// <summary>
        /// Determines whether a sub-namespace exists.
        /// </summary>
        /// <param name="name">Namespace name, mandatory.</param>
        /// <returns>true if it exists, otherwise false.</returns>
        bool NamespaceExists(string name);

        /// <summary>
        /// Gets the direct contents of this namespace.
        /// </summary>
        NamespaceListing GetListing();

        /// <summary>
        /// Gets the contents of this namespace.
        /// </summary>
        /// <param name="deep">Include the whole sub-tree.</param>
        NamespaceListing GetListing(bool deep);
    }
}
