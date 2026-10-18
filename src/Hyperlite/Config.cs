namespace Hyperlite
{
    using System;

    /// <summary>
    /// The configuration property names the client understands.
    /// </summary>
    /// <remarks>
    /// Names, aliases and provider names derive from ht4c::Common::Config. They appear
    /// verbatim in connection strings and in <see cref="IContext.Properties" />, so they
    /// are part of the public contract.
    /// <para>
    /// The prefix used to be <c>Ht4n.</c> and is now <c>Hyperlite.</c>. Both spellings
    /// are accepted wherever a property is read - a connection string, a configuration
    /// file, a property dictionary - and both are present in
    /// <see cref="IContext.Properties" /> afterwards, with the same value, exactly as
    /// the short aliases already were. So a connection string written against the old
    /// prefix keeps working, and so does code reading a property back under it.
    /// </para>
    /// </remarks>
    public static class Config
    {
        /// <summary>
        /// The prefix every property name carries.
        /// </summary>
        internal const string Prefix = "Hyperlite.";

        /// <summary>
        /// The prefix properties carried before the library was renamed. Still accepted,
        /// see the remarks on <see cref="Config" />.
        /// </summary>
        internal const string LegacyPrefix = "Ht4n.";

        /// <summary>Provider name property.</summary>
        public const string ProviderName = "Hyperlite.Provider";

        /// <summary>Short form of <see cref="ProviderName" />.</summary>
        public const string ProviderNameAlias = "Provider";

        /// <summary>Provider name value for the SQLite provider.</summary>
        public const string ProviderSQLite = "SQLite";

        /// <summary>
        /// Uri property, the database file as a <c>file://</c> uri: <c>file://db.sqlite</c> relative
        /// to the working directory, <c>file:///c/data/db.sqlite</c> on Windows,
        /// <c>file:///var/data/db.sqlite</c> elsewhere.
        /// </summary>
        public const string Uri = "Hyperlite.Uri";

        /// <summary>Short form of <see cref="Uri" />.</summary>
        public const string UriAlias = "Uri";

        /// <summary>Connection timeout property, in milliseconds.</summary>
        public const string ConnectionTimeout = "Hyperlite.ConnectionTimeout";

        /// <summary>Short form of <see cref="ConnectionTimeout" />.</summary>
        public const string ConnectionTimeoutAlias = "ConnectionTimeout";

        /// <summary>
        /// The database file, relative to the working directory. A <c>file://</c> <see cref="Uri" />
        /// sets it too; <c>memory</c> opens a database in memory instead.
        /// </summary>
        public const string SQLiteFilename = "Hyperlite.SQLite.Filename";

        /// <summary>SQLite page cache size, in megabytes.</summary>
        public const string SQLiteCacheSizeMB = "Hyperlite.SQLite.CacheSizeMB";

        /// <summary>SQLite page size, in kilobytes.</summary>
        public const string SQLitePageSizeKB = "Hyperlite.SQLite.PageSizeKB";

        /// <summary>
        /// Journals writes in a write-ahead log rather than the rollback journal. Off by default;
        /// once on, the database file records it.
        /// </summary>
        public const string SQLiteWriteAheadLog = "Hyperlite.SQLite.WAL";

        /// <summary>
        /// SQLite synchronous writes, off by default: sqlite then leaves the syncing to the
        /// operating system, which is fast, but a power cut or an operating system crash can
        /// corrupt the database. On, a commit waits until it is on disk.
        /// </summary>
        public const string SQLiteSynchronous = "Hyperlite.SQLite.Synchronous";

        /// <summary>SQLite auto-vacuum mode: 0 none, 1 full, 2 incremental.</summary>
        public const string SQLiteAutoVacuum = "Hyperlite.SQLite.AutoVacuum";

        /// <summary>
        /// A table created with it on holds one cell per row: the row is its primary key.
        /// </summary>
        public const string SQLiteUniqueRows = "Hyperlite.SQLite.UniqueRows";

        /// <summary>
        /// A table created with it on keeps one revision of a cell, the last written, not one per
        /// timestamp.
        /// </summary>
        public const string SQLiteNoCellRevisions = "Hyperlite.SQLite.NoCellRevisions";

        /// <summary>
        /// A table created with it on gets an index on column family and qualifier together.
        /// </summary>
        public const string SQLiteIndexColumn = "Hyperlite.SQLite.Index.Column";

        /// <summary>
        /// A table created with it on gets an index on the column family.
        /// </summary>
        public const string SQLiteIndexColumnFamily = "Hyperlite.SQLite.Index.ColumnFamily";

        /// <summary>
        /// A table created with it on gets an index on the column qualifier.
        /// </summary>
        public const string SQLiteIndexColumnQualifier = "Hyperlite.SQLite.Index.ColumnQualifier";

        /// <summary>
        /// A table created with it on gets an index on the timestamp.
        /// </summary>
        public const string SQLiteIndexTimestamp = "Hyperlite.SQLite.Index.Timestamp";

        /// <summary>
        /// The pre-rename spelling of a property name, or null when there is none.
        /// </summary>
        /// <remarks>
        /// Derived rather than tabulated, so a property added later answers to its old
        /// prefix without anyone having to remember to list it - and so the two spellings
        /// cannot drift apart.
        /// </remarks>
        internal static string ToLegacyName(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return null;
            }

            return LegacyPrefix + name.Substring(Prefix.Length);
        }
    }
}
