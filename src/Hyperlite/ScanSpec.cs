namespace Hyperlite
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.Text;

    using Hyperlite.Interop;

    /// <summary>
    /// Represents a scan specification: what a scanner returns and how much of it.
    /// </summary>
    /// <remarks>
    /// The Add methods return this instance, so a specification reads as a chain.
    /// </remarks>
    /// <seealso cref="ITable.CreateScanner(ScanSpec)" />
    [Serializable]
    public sealed class ScanSpec
    {
        /// <summary>
        /// The hypertable timestamp epoch.
        /// </summary>
        private static readonly DateTime TimestampOrigin =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly bool isSorted;

        private ICollection<string> rows;

        private ISet<string> columns;

        private ISet<ColumnPredicate> columnPredicates;

        private ICollection<Key> keys;

        private ICollection<RowInterval> rowIntervals;

        private ICollection<CellInterval> cellIntervals;

        public ScanSpec()
        {
        }

        /// <param name="sorted">If true the rows, cells and intervals are kept sorted and
        /// de-duplicated, otherwise in insertion order.</param>
        public ScanSpec(bool sorted)
        {
            this.isSorted = sorted;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScanSpec" /> class for a single row.
        /// </summary>
        /// <param name="row">Row key.</param>
        public ScanSpec(string row)
        {
            this.AddRow(row);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScanSpec" /> class for a single cell.
        /// </summary>
        /// <param name="key">Cell key. A non-zero timestamp additionally narrows the scan
        /// to that exact version.</param>
        public ScanSpec(Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.AddCell(key);
            if (key.Timestamp > 0)
            {
                this.MaxCells = 1;
                this.StartTimestamp = key.Timestamp;
                this.EndTimestamp = key.Timestamp;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScanSpec" /> class for a single
        /// column predicate.
        /// </summary>
        public ScanSpec(ColumnPredicate columnPredicate)
        {
            this.AddColumnPredicate(columnPredicate);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScanSpec" /> class for a single
        /// row interval.
        /// </summary>
        public ScanSpec(RowInterval rowInterval)
        {
            this.AddRowInterval(rowInterval);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScanSpec" /> class for a single
        /// cell interval.
        /// </summary>
        public ScanSpec(CellInterval cellInterval)
        {
            this.AddCellInterval(cellInterval);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScanSpec" /> class that is a copy
        /// of the specified instance.
        /// </summary>
        /// <param name="scanSpec">Scan specification to copy.</param>
        public ScanSpec(ScanSpec scanSpec)
        {
            if (scanSpec == null)
            {
                throw new ArgumentNullException(nameof(scanSpec));
            }

            this.MaxRows = scanSpec.MaxRows;
            this.MaxVersions = scanSpec.MaxVersions;
            this.MaxCells = scanSpec.MaxCells;
            this.MaxCellsColumnFamily = scanSpec.MaxCellsColumnFamily;
            this.RowOffset = scanSpec.RowOffset;
            this.CellOffset = scanSpec.CellOffset;
            this.KeysOnly = scanSpec.KeysOnly;
            this.NotUseQueryCache = scanSpec.NotUseQueryCache;
            this.ScanAndFilter = scanSpec.ScanAndFilter;
            this.ColumnPredicateAnd = scanSpec.ColumnPredicateAnd;
            this.StartTimestamp = scanSpec.StartTimestamp;
            this.EndTimestamp = scanSpec.EndTimestamp;
            this.RowRegex = scanSpec.RowRegex;
            this.ValueRegex = scanSpec.ValueRegex;
            this.Timeout = scanSpec.Timeout;
            this.Flags = scanSpec.Flags;
            this.isSorted = scanSpec.isSorted;

            if (scanSpec.rows != null)
            {
                this.AddRow(scanSpec.rows);
            }

            if (scanSpec.columns != null)
            {
                this.AddColumn(scanSpec.columns);
            }

            if (scanSpec.keys != null)
            {
                this.AddCell(scanSpec.keys);
            }

            if (scanSpec.columnPredicates != null)
            {
                this.AddColumnPredicate(scanSpec.columnPredicates);
            }

            if (scanSpec.rowIntervals != null)
            {
                this.AddRowInterval(scanSpec.rowIntervals);
            }

            if (scanSpec.cellIntervals != null)
            {
                this.AddCellInterval(scanSpec.cellIntervals);
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of rows to return, or 0 for no limit.
        /// </summary>
        public int MaxRows { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of cell versions to return, or 0 for the
        /// column family's own limit.
        /// </summary>
        public int MaxVersions { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of cells to return, or 0 for no limit.
        /// </summary>
        public int MaxCells { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of cells to return per column family, or 0 for
        /// no limit.
        /// </summary>
        public int MaxCellsColumnFamily { get; set; }

        /// <summary>
        /// Gets or sets the number of rows to skip. Mutually exclusive with
        /// <see cref="CellOffset" />.
        /// </summary>
        public int RowOffset { get; set; }

        /// <summary>
        /// Gets or sets the number of cells to skip. Mutually exclusive with
        /// <see cref="RowOffset" />.
        /// </summary>
        public int CellOffset { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the scanner returns keys without values.
        /// </summary>
        public bool KeysOnly { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the query cache is bypassed.
        /// </summary>
        public bool NotUseQueryCache { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the requested rows are looked up one at
        /// a time instead of scanning the range between them.
        /// </summary>
        /// <remarks>
        /// Only takes effect where there is something to filter, that is more than one row
        /// or at least one interval.
        /// </remarks>
        public bool ScanAndFilter { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the column predicates are combined with
        /// AND instead of OR. Only takes effect with more than one predicate.
        /// </summary>
        public bool ColumnPredicateAnd { get; set; }

        /// <summary>
        /// Gets or sets the inclusive lower bound of the timestamp range.
        /// </summary>
        public DateTime StartDateTime
        {
            get
            {
                return TimestampOrigin + TimeSpan.FromTicks((long)(this.StartTimestamp / 100));
            }

            set
            {
                if (value < TimestampOrigin)
                {
                    throw new ArgumentException("Invalid DateTime");
                }

                if (value.Kind == DateTimeKind.Unspecified)
                {
                    throw new ArgumentException("Unspecified DateTime Kind");
                }

                this.StartTimestamp = (ulong)((value.ToUniversalTime() - TimestampOrigin).Ticks * 100);
            }
        }

        /// <summary>
        /// Gets or sets the inclusive lower bound of the timestamp range, in nanoseconds
        /// since 1970-01-01 00:00:00.0 UTC. 0 means no lower bound.
        /// </summary>
        [CLSCompliant(false)]
        public ulong StartTimestamp { get; set; }

        /// <summary>
        /// Gets or sets the inclusive upper bound of the timestamp range.
        /// </summary>
        public DateTime EndDateTime
        {
            get
            {
                return TimestampOrigin + TimeSpan.FromTicks((long)(this.EndTimestamp / 100));
            }

            set
            {
                if (value < TimestampOrigin)
                {
                    throw new ArgumentException("Invalid DateTime");
                }

                if (value.Kind == DateTimeKind.Unspecified)
                {
                    throw new ArgumentException("Unspecified DateTime Kind");
                }

                this.EndTimestamp = (ulong)((value.ToUniversalTime() - TimestampOrigin).Ticks * 100);
            }
        }

        /// <summary>
        /// Gets or sets the inclusive upper bound of the timestamp range, in nanoseconds
        /// since 1970-01-01 00:00:00.0 UTC. 0 means no upper bound.
        /// </summary>
        [CLSCompliant(false)]
        public ulong EndTimestamp { get; set; }

        /// <summary>
        /// Gets or sets the regular expression the row key has to match.
        /// </summary>
        public string RowRegex { get; set; }

        /// <summary>
        /// Gets or sets the regular expression the cell value has to match.
        /// </summary>
        public string ValueRegex { get; set; }

        /// <summary>
        /// Gets or sets Hypertable's limit on the time scanner methods may take; the SQLite
        /// store has none, so this changes nothing but that a negative one is refused. The
        /// limit on a regular expression match, see SECURITY.md, is a separate one.
        /// </summary>
        public TimeSpan Timeout { get; set; }

        /// <summary>
        /// Kept from Hypertable's api: no flag changes what the SQLite store does.
        /// </summary>
        public ScannerFlags Flags { get; set; }

        /// <summary>
        /// Gets the number of rows added.
        /// </summary>
        public int RowCount
        {
            get
            {
                return this.rows != null ? this.rows.Count : 0;
            }
        }

        /// <summary>
        /// Gets the number of columns added.
        /// </summary>
        public int ColumnCount
        {
            get
            {
                return this.columns != null ? this.columns.Count : 0;
            }
        }

        /// <summary>
        /// Gets the number of column predicates added.
        /// </summary>
        public int ColumnPredicateCount
        {
            get
            {
                return this.columnPredicates != null ? this.columnPredicates.Count : 0;
            }
        }

        /// <summary>
        /// Gets the number of cells added.
        /// </summary>
        public int CellCount
        {
            get
            {
                return this.keys != null ? this.keys.Count : 0;
            }
        }

        /// <summary>
        /// Gets the number of row intervals added.
        /// </summary>
        public int RowIntervalCount
        {
            get
            {
                return this.rowIntervals != null ? this.rowIntervals.Count : 0;
            }
        }

        /// <summary>
        /// Gets the number of cell intervals added.
        /// </summary>
        public int CellIntervalCount
        {
            get
            {
                return this.cellIntervals != null ? this.cellIntervals.Count : 0;
            }
        }

        /// <summary>
        /// Gets the rows added.
        /// </summary>
        public ReadOnlyCollection<string> Rows
        {
            get
            {
                if (this.rows == null)
                {
                    this.rows = this.CreateCollection<string>();
                }

                return AsReadOnly(this.rows);
            }
        }

        /// <summary>
        /// Gets the columns added.
        /// </summary>
        public ReadOnlyCollection<string> Columns
        {
            get
            {
                if (this.columns == null)
                {
                    this.columns = new SortedSet<string>();
                }

                return AsReadOnly(this.columns);
            }
        }

        /// <summary>
        /// Gets the column predicates added.
        /// </summary>
        public ReadOnlyCollection<ColumnPredicate> ColumnPredicates
        {
            get
            {
                if (this.columnPredicates == null)
                {
                    this.columnPredicates = new HashSet<ColumnPredicate>();
                }

                return AsReadOnly(this.columnPredicates);
            }
        }

        /// <summary>
        /// Gets the cells added.
        /// </summary>
        public ReadOnlyCollection<Key> Cells
        {
            get
            {
                if (this.keys == null)
                {
                    this.keys = this.CreateCollection<Key>();
                }

                return AsReadOnly(this.keys);
            }
        }

        /// <summary>
        /// Gets the row intervals added.
        /// </summary>
        public ReadOnlyCollection<RowInterval> RowIntervals
        {
            get
            {
                if (this.rowIntervals == null)
                {
                    this.rowIntervals = this.CreateCollection<RowInterval>();
                }

                return AsReadOnly(this.rowIntervals);
            }
        }

        /// <summary>
        /// Gets the cell intervals added.
        /// </summary>
        public ReadOnlyCollection<CellInterval> CellIntervals
        {
            get
            {
                if (this.cellIntervals == null)
                {
                    this.cellIntervals = this.CreateCollection<CellInterval>();
                }

                return AsReadOnly(this.cellIntervals);
            }
        }

        /// <summary>
        /// Reduces a set of columns so that a column family covered by a bare family entry
        /// drops its qualified entries.
        /// </summary>
        /// <param name="source">Columns to reduce, or null.</param>
        /// <returns>The reduced set, or null when source was null.</returns>
        public static ISet<string> DistinctColumn(IEnumerable<string> source)
        {
            if (source == null)
            {
                return null;
            }

            var sorted = new SortedSet<string>(source);
            var distinct = new SortedSet<string>();
            foreach (var column in sorted)
            {
                var colon = column.IndexOf(':');
                if (!distinct.Contains(colon < 0 ? column : column.Substring(0, colon)))
                {
                    distinct.Add(column);
                }
            }

            return distinct;
        }

        /// <summary>
        /// Reduces a set of columns and reports the column families involved.
        /// </summary>
        /// <param name="source">Columns to reduce, or null.</param>
        /// <param name="columnFamilies">Receives the column families.</param>
        /// <returns>The reduced set, or null when source was null.</returns>
        public static ISet<string> DistinctColumn(IEnumerable<string> source, out ISet<string> columnFamilies)
        {
            columnFamilies = null;
            if (source == null)
            {
                return null;
            }

            columnFamilies = new SortedSet<string>();
            var sorted = new SortedSet<string>(source);
            var distinct = new SortedSet<string>();
            foreach (var column in sorted)
            {
                var colon = column.IndexOf(':');
                var family = colon < 0 ? column : column.Substring(0, colon);
                if (!distinct.Contains(family))
                {
                    distinct.Add(column);
                    columnFamilies.Add(family);
                }
            }

            return distinct;
        }

        /// <summary>
        /// Adds one or more rows.
        /// </summary>
        /// <param name="row">Row key, mandatory.</param>
        /// <param name="moreRows">Further row keys.</param>
        public ScanSpec AddRow(string row, params string[] moreRows)
        {
            if (string.IsNullOrEmpty(row))
            {
                throw new ArgumentNullException(nameof(row));
            }

            if (this.rows == null)
            {
                this.rows = this.CreateCollection<string>();
            }

            this.rows.Add(row);

            if (moreRows != null)
            {
                foreach (var more in moreRows)
                {
                    this.AddRow(more);
                }
            }

            return this;
        }

        /// <summary>
        /// Adds a sequence of rows.
        /// </summary>
        /// <param name="rows">Row keys.</param>
        public ScanSpec AddRow(IEnumerable<string> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            if (this.rows == null)
            {
                this.rows = this.CreateCollection<string>();
            }

            var list = this.rows as List<string>;
            if (list == null)
            {
                foreach (var row in rows)
                {
                    this.rows.Add(row);
                }
            }
            else
            {
                list.AddRange(rows);
            }

            return this;
        }

        /// <param name="row">Row key, mandatory.</param>
        public ScanSpec RemoveRow(string row)
        {
            if (string.IsNullOrEmpty(row))
            {
                throw new ArgumentNullException(nameof(row));
            }

            if (this.rows != null)
            {
                this.rows.Remove(row);
            }

            return this;
        }

        /// <summary>
        /// Determines whether a row has been added.
        /// </summary>
        /// <param name="row">Row key, mandatory.</param>
        /// <returns>true if the row has been added, otherwise false.</returns>
        public bool ContainsRow(string row)
        {
            if (string.IsNullOrEmpty(row))
            {
                throw new ArgumentNullException(nameof(row));
            }

            return this.rows != null && this.rows.Contains(row);
        }

        /// <summary>
        /// Adds one or more columns, each either a column family or family:qualifier.
        /// </summary>
        /// <param name="column">Column, mandatory.</param>
        /// <param name="moreColumns">Further columns.</param>
        public ScanSpec AddColumn(string column, params string[] moreColumns)
        {
            if (string.IsNullOrEmpty(column))
            {
                throw new ArgumentNullException(nameof(column));
            }

            if (this.columns == null)
            {
                this.columns = new SortedSet<string>();
            }

            this.columns.Add(column);

            if (moreColumns != null)
            {
                foreach (var more in moreColumns)
                {
                    this.AddColumn(more);
                }
            }

            return this;
        }

        /// <summary>
        /// Adds a sequence of columns.
        /// </summary>
        public ScanSpec AddColumn(IEnumerable<string> columns)
        {
            if (columns == null)
            {
                throw new ArgumentNullException(nameof(columns));
            }

            foreach (var column in columns)
            {
                this.AddColumn(column);
            }

            return this;
        }

        /// <param name="column">Column, mandatory.</param>
        public ScanSpec RemoveColumn(string column)
        {
            if (string.IsNullOrEmpty(column))
            {
                throw new ArgumentNullException(nameof(column));
            }

            if (this.columns != null)
            {
                this.columns.Remove(column);
            }

            return this;
        }

        /// <summary>
        /// Adds one or more column predicates.
        /// </summary>
        /// <param name="columnPredicate">Column predicate, mandatory.</param>
        /// <param name="moreColumnPredicates">Further column predicates.</param>
        public ScanSpec AddColumnPredicate(ColumnPredicate columnPredicate, params ColumnPredicate[] moreColumnPredicates)
        {
            if (columnPredicate == null)
            {
                throw new ArgumentNullException(nameof(columnPredicate));
            }

            if (string.IsNullOrEmpty(columnPredicate.ColumnFamily))
            {
                throw new ArgumentException("Invalid column family in column predicate", nameof(columnPredicate));
            }

            if (columnPredicate.Match == MatchKind.Undefined)
            {
                throw new ArgumentException("Invalid match kind in column predicate", nameof(columnPredicate));
            }

            if (this.columnPredicates == null)
            {
                this.columnPredicates = new HashSet<ColumnPredicate>();
            }

            this.columnPredicates.Add(columnPredicate);

            if (moreColumnPredicates != null)
            {
                foreach (var more in moreColumnPredicates)
                {
                    this.AddColumnPredicate(more);
                }
            }

            return this;
        }

        /// <summary>
        /// Adds a sequence of column predicates.
        /// </summary>
        public ScanSpec AddColumnPredicate(IEnumerable<ColumnPredicate> columnPredicates)
        {
            if (columnPredicates == null)
            {
                throw new ArgumentNullException(nameof(columnPredicates));
            }

            foreach (var columnPredicate in columnPredicates)
            {
                this.AddColumnPredicate(columnPredicate);
            }

            return this;
        }

        /// <param name="columnPredicate">Column predicate, mandatory.</param>
        public ScanSpec RemoveColumnPredicate(ColumnPredicate columnPredicate)
        {
            if (columnPredicate == null)
            {
                throw new ArgumentNullException(nameof(columnPredicate));
            }

            if (this.columnPredicates != null)
            {
                this.columnPredicates.Remove(columnPredicate);
            }

            return this;
        }

        /// <summary>
        /// Removes all column predicates.
        /// </summary>
        public ScanSpec ClearColumnPredicates()
        {
            if (this.columnPredicates != null)
            {
                this.columnPredicates.Clear();
            }

            return this;
        }

        /// <param name="row">Row key, mandatory.</param>
        /// <param name="columnFamily">Column family, mandatory.</param>
        /// <param name="columnQualifier">Column qualifier, might be null.</param>
        public ScanSpec AddCell(string row, string columnFamily, string columnQualifier)
        {
            this.AddCell(new Key(row, columnFamily, columnQualifier));
            return this;
        }

        /// <summary>
        /// Adds one or more cells.
        /// </summary>
        /// <param name="key">Cell key, mandatory. Row and column family are required.</param>
        /// <param name="moreKeys">Further cell keys.</param>
        public ScanSpec AddCell(Key key, params Key[] moreKeys)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (string.IsNullOrEmpty(key.Row))
            {
                throw new ArgumentException("Invalid parameter key (key.Row null or empty)", nameof(key));
            }

            if (string.IsNullOrEmpty(key.ColumnFamily))
            {
                throw new ArgumentException("Invalid parameter key (key.ColumnFamily null or empty)", nameof(key));
            }

            if (this.keys == null)
            {
                this.keys = this.CreateCollection<Key>();
            }

            this.keys.Add(key);

            if (moreKeys != null)
            {
                foreach (var more in moreKeys)
                {
                    this.AddCell(more);
                }
            }

            return this;
        }

        /// <summary>
        /// Adds a sequence of cells.
        /// </summary>
        /// <param name="keys">Cell keys.</param>
        public ScanSpec AddCell(IEnumerable<Key> keys)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            foreach (var key in keys)
            {
                this.AddCell(key);
            }

            return this;
        }

        /// <param name="row">Row key, mandatory.</param>
        /// <param name="columnFamily">Column family, mandatory.</param>
        /// <param name="columnQualifier">Column qualifier, might be null.</param>
        public ScanSpec RemoveCell(string row, string columnFamily, string columnQualifier)
        {
            this.RemoveCell(new Key(row, columnFamily, columnQualifier));
            return this;
        }

        /// <param name="key">Cell key, mandatory.</param>
        public ScanSpec RemoveCell(Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (string.IsNullOrEmpty(key.Row))
            {
                throw new ArgumentException("Invalid parameter key (key.Row null or empty)", nameof(key));
            }

            if (string.IsNullOrEmpty(key.ColumnFamily))
            {
                throw new ArgumentException("Invalid parameter key (key.ColumnFamily null or empty)", nameof(key));
            }

            if (this.keys != null)
            {
                this.keys.Remove(key);
            }

            return this;
        }

        /// <summary>
        /// Adds one or more row intervals.
        /// </summary>
        /// <param name="rowInterval">Row interval, mandatory.</param>
        /// <param name="moreRowIntervals">Further row intervals.</param>
        public ScanSpec AddRowInterval(RowInterval rowInterval, params RowInterval[] moreRowIntervals)
        {
            if (rowInterval == null)
            {
                throw new ArgumentNullException(nameof(rowInterval));
            }

            if (this.rowIntervals == null)
            {
                this.rowIntervals = this.CreateCollection<RowInterval>();
            }

            this.rowIntervals.Add(rowInterval);

            if (moreRowIntervals != null)
            {
                foreach (var more in moreRowIntervals)
                {
                    this.AddRowInterval(more);
                }
            }

            return this;
        }

        /// <summary>
        /// Adds a sequence of row intervals.
        /// </summary>
        public ScanSpec AddRowInterval(IEnumerable<RowInterval> rowIntervals)
        {
            if (rowIntervals == null)
            {
                throw new ArgumentNullException(nameof(rowIntervals));
            }

            foreach (var rowInterval in rowIntervals)
            {
                this.AddRowInterval(rowInterval);
            }

            return this;
        }

        /// <param name="rowInterval">Row interval, mandatory.</param>
        public ScanSpec RemoveRowInterval(RowInterval rowInterval)
        {
            if (rowInterval == null)
            {
                throw new ArgumentNullException(nameof(rowInterval));
            }

            if (this.rowIntervals != null)
            {
                this.rowIntervals.Remove(rowInterval);
            }

            return this;
        }

        /// <summary>
        /// Removes all row intervals.
        /// </summary>
        public ScanSpec ClearRowIntervals()
        {
            if (this.rowIntervals != null)
            {
                this.rowIntervals.Clear();
            }

            return this;
        }

        /// <summary>
        /// Adds one or more cell intervals.
        /// </summary>
        /// <param name="cellInterval">Cell interval, mandatory. Both column families are required.</param>
        /// <param name="moreCellIntervals">Further cell intervals.</param>
        public ScanSpec AddCellInterval(CellInterval cellInterval, params CellInterval[] moreCellIntervals)
        {
            if (cellInterval == null)
            {
                throw new ArgumentNullException(nameof(cellInterval));
            }

            if (string.IsNullOrEmpty(cellInterval.StartColumnFamily))
            {
                throw new ArgumentException(
                    "Invalid parameter cellInterval (cellInterval.StartColumnFamily null or empty)", nameof(cellInterval));
            }

            if (string.IsNullOrEmpty(cellInterval.EndColumnFamily))
            {
                throw new ArgumentException(
                    "Invalid parameter cellInterval (cellInterval.EndColumnFamily null or empty)", nameof(cellInterval));
            }

            if (this.cellIntervals == null)
            {
                this.cellIntervals = this.CreateCollection<CellInterval>();
            }

            this.cellIntervals.Add(cellInterval);

            if (moreCellIntervals != null)
            {
                foreach (var more in moreCellIntervals)
                {
                    this.AddCellInterval(more);
                }
            }

            return this;
        }

        /// <summary>
        /// Adds a sequence of cell intervals.
        /// </summary>
        public ScanSpec AddCellInterval(IEnumerable<CellInterval> cellIntervals)
        {
            if (cellIntervals == null)
            {
                throw new ArgumentNullException(nameof(cellIntervals));
            }

            foreach (var cellInterval in cellIntervals)
            {
                this.AddCellInterval(cellInterval);
            }

            return this;
        }

        /// <param name="cellInterval">Cell interval, mandatory. All four bounds are required.</param>
        public ScanSpec RemoveCellInterval(CellInterval cellInterval)
        {
            if (cellInterval == null)
            {
                throw new ArgumentNullException(nameof(cellInterval));
            }

            if (string.IsNullOrEmpty(cellInterval.StartRow))
            {
                throw new ArgumentException(
                    "Invalid parameter cellInterval (cellInterval.StartRow null or empty)", nameof(cellInterval));
            }

            if (string.IsNullOrEmpty(cellInterval.StartColumnFamily))
            {
                throw new ArgumentException(
                    "Invalid parameter cellInterval (cellInterval.StartColumnFamily null or empty)", nameof(cellInterval));
            }

            if (string.IsNullOrEmpty(cellInterval.EndRow))
            {
                throw new ArgumentException(
                    "Invalid parameter cellInterval (cellInterval.EndRow null or empty)", nameof(cellInterval));
            }

            if (string.IsNullOrEmpty(cellInterval.EndColumnFamily))
            {
                throw new ArgumentException(
                    "Invalid parameter cellInterval (cellInterval.EndColumnFamily null or empty)", nameof(cellInterval));
            }

            if (this.cellIntervals != null)
            {
                this.cellIntervals.Remove(cellInterval);
            }

            return this;
        }

        /// <summary>
        /// Removes all cell intervals.
        /// </summary>
        public ScanSpec ClearCellIntervals()
        {
            if (this.cellIntervals != null)
            {
                this.cellIntervals.Clear();
            }

            return this;
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(this.GetType());
            sb.Append("(");

            AppendInt(sb, "MaxRows", this.MaxRows);
            AppendInt(sb, "MaxVersions", this.MaxVersions);
            AppendInt(sb, "MaxCells", this.MaxCells);
            AppendInt(sb, "MaxCellsColumnFamily", this.MaxCellsColumnFamily);
            AppendInt(sb, "RowOffset", this.RowOffset);
            AppendInt(sb, "CellOffset", this.CellOffset);
            AppendBool(sb, "KeysOnly", this.KeysOnly);
            AppendBool(sb, "NotUseQueryCache", this.NotUseQueryCache);
            AppendBool(sb, "ScanAndFilter", this.ScanAndFilter);
            AppendDateTime(sb, "StartDateTime", this.StartDateTime);
            AppendDateTime(sb, "EndDateTime", this.EndDateTime);
            AppendString(sb, "RowRegex", this.RowRegex);
            AppendString(sb, "ValueRegex", this.ValueRegex);

            // ht4n compared Timeout.Ticks against the epoch's tick count, which no
            // plausible timeout reaches, so the timeout was never printed. Kept, rather
            // than adding a field to a format callers might be parsing.
            AppendInt(sb, "RowCount", this.RowCount);
            AppendInt(sb, "ColumnCount", this.ColumnCount);

            if (this.ColumnCount > 0)
            {
                sb.Append("Columns=[");
                foreach (var column in this.columns)
                {
                    sb.Append(column);
                    sb.Append(", ");
                }

                sb.Length -= 2;
                sb.Append("], ");
            }

            if (this.ColumnPredicateCount > 0)
            {
                sb.Append("ColumnPredicates=[");
                foreach (var columnPredicate in this.columnPredicates)
                {
                    sb.Append(columnPredicate.ToString());
                    sb.Append(", ");
                }

                sb.Length -= 2;
                sb.Append("], ");
            }

            AppendInt(sb, "CellCount", this.CellCount);
            AppendInt(sb, "RowIntervalCount", this.RowIntervalCount);
            AppendInt(sb, "CellIntervalCount", this.CellIntervalCount);

            sb.Append(string.Format(CultureInfo.InvariantCulture, "Flags={0}", this.Flags));
            sb.Append(")");
            return sb.ToString();
        }

        /// <summary>
        /// Transfers this specification into a native scan spec handle.
        /// </summary>
        internal unsafe void To(ScanSpecHandle handle)
        {
            if (this.MaxRows > 0)
            {
                handle.SetInt(Abi.SsMaxRows, this.MaxRows);
            }

            if (this.MaxVersions > 0)
            {
                handle.SetInt(Abi.SsMaxVersions, this.MaxVersions);
            }

            if (this.MaxCells > 0)
            {
                handle.SetInt(Abi.SsMaxCells, this.MaxCells);
            }

            if (this.MaxCellsColumnFamily > 0)
            {
                handle.SetInt(Abi.SsMaxCellsColumnFamily, this.MaxCellsColumnFamily);
            }

            // mutually exclusive down below, so row offset wins as it did in ht4n
            if (this.RowOffset > 0)
            {
                handle.SetInt(Abi.SsRowOffset, this.RowOffset);
            }
            else if (this.CellOffset > 0)
            {
                handle.SetInt(Abi.SsCellOffset, this.CellOffset);
            }

            handle.SetInt(Abi.SsKeysOnly, this.KeysOnly ? 1 : 0);
            handle.SetInt(Abi.SsNotUseQueryCache, this.NotUseQueryCache ? 1 : 0);

            // scan-and-filter only means something where there is more than one row, or
            // an interval, to filter against
            var scanAndFilter = this.ScanAndFilter
                                && ((this.rows != null && this.rows.Count > 1)
                                    || (this.rowIntervals != null && this.rowIntervals.Count > 0)
                                    || (this.cellIntervals != null && this.cellIntervals.Count > 0));
            handle.SetInt(Abi.SsScanAndFilter, scanAndFilter ? 1 : 0);

            var predicateAnd = this.ColumnPredicateAnd
                               && this.columnPredicates != null
                               && this.columnPredicates.Count > 1;
            handle.SetInt(Abi.SsColumnPredicateAnd, predicateAnd ? 1 : 0);

            if (this.StartTimestamp > 0)
            {
                handle.SetInt(Abi.SsStartTimestamp, (long)this.StartTimestamp);
            }

            if (this.EndTimestamp > 0)
            {
                handle.SetInt(Abi.SsEndTimestamp, (long)this.EndTimestamp);
            }

            if (!string.IsNullOrEmpty(this.RowRegex))
            {
                handle.SetString(Abi.SsRowRegex, this.RowRegex);
            }

            if (!string.IsNullOrEmpty(this.ValueRegex))
            {
                handle.SetString(Abi.SsValueRegex, this.ValueRegex);
            }

            if (this.rows != null && this.rows.Count > 0)
            {
                handle.SetInt(Abi.SsReserveRows, this.rows.Count);
                foreach (var row in this.rows)
                {
                    handle.AddRow(row);
                }
            }

            if (this.columnPredicates != null && this.columnPredicates.Count > 0)
            {
                foreach (var columnPredicate in this.columnPredicates)
                {
                    if (string.IsNullOrEmpty(columnPredicate.ColumnFamily))
                    {
                        throw new BadScanSpecException("Invalid column family in column predicate");
                    }

                    if (columnPredicate.Match == MatchKind.Undefined)
                    {
                        throw new BadScanSpecException("Invalid match kind in column predicate");
                    }

                    handle.AddColumnPredicate(
                        columnPredicate.ColumnFamily,
                        columnPredicate.ColumnQualifier,
                        (int)columnPredicate.Match,
                        columnPredicate.SearchValue);
                }
            }

            if (this.columns != null && this.columns.Count > 0)
            {
                handle.SetInt(Abi.SsReserveColumns, this.columns.Count);

                // A bare column family covers every qualifier of it, so once one has been
                // added its qualified siblings are redundant. The set is sorted, and a
                // family sorts before "family:qualifier", so a single pass suffices.
                var families = new HashSet<string>();
                foreach (var column in this.columns)
                {
                    var colon = column.IndexOf(':');
                    var family = colon < 0 ? column : column.Substring(0, colon);
                    if (!families.Contains(family))
                    {
                        handle.AddColumn(column);
                        if (colon < 0)
                        {
                            families.Add(family);
                        }
                    }
                }
            }

            if (this.keys != null && this.keys.Count > 0)
            {
                handle.SetInt(Abi.SsReserveCells, this.keys.Count);
                foreach (var key in this.keys)
                {
                    var column = key.ColumnQualifier == null
                        ? key.ColumnFamily
                        : key.ColumnFamily + ":" + key.ColumnQualifier;
                    handle.AddCell(key.Row, column);
                }
            }

            if (this.rowIntervals != null)
            {
                foreach (var rowInterval in this.rowIntervals)
                {
                    handle.AddRowInterval(
                        rowInterval.StartRow,
                        rowInterval.IncludeStartRow,
                        rowInterval.EndRow,
                        rowInterval.IncludeEndRow);
                }
            }

            if (this.cellIntervals != null)
            {
                foreach (var cellInterval in this.cellIntervals)
                {
                    var startColumn = cellInterval.StartColumnQualifier == null
                        ? cellInterval.StartColumnFamily
                        : cellInterval.StartColumnFamily + ":" + cellInterval.StartColumnQualifier;

                    // Mind the condition: the end column follows StartColumnQualifier, not
                    // EndColumnQualifier. ht4n did the same and the behaviour is observable,
                    // so it is preserved rather than corrected.
                    var endColumn = cellInterval.StartColumnQualifier == null
                        ? cellInterval.EndColumnFamily
                        : cellInterval.EndColumnFamily + ":" + cellInterval.EndColumnQualifier;

                    handle.AddCellInterval(
                        cellInterval.StartRow,
                        startColumn,
                        cellInterval.IncludeStartRow,
                        cellInterval.EndRow,
                        endColumn,
                        cellInterval.IncludeEndRow);
                }
            }
        }

        private static ReadOnlyCollection<T> AsReadOnly<T>(ICollection<T> collection)
        {
            var list = collection as List<T>;
            if (list == null)
            {
                list = new List<T>(collection);
            }

            return list.AsReadOnly();
        }

        private static void AppendInt(StringBuilder sb, string name, int value)
        {
            if (value > 0)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}={1}, ", name, value));
            }
        }

        private static void AppendBool(StringBuilder sb, string name, bool value)
        {
            if (value)
            {
                sb.Append(name);
                sb.Append(", ");
            }
        }

        private static void AppendString(StringBuilder sb, string name, string value)
        {
            if (value != null)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}={1}, ", name, value));
            }
        }

        private static void AppendDateTime(StringBuilder sb, string name, DateTime value)
        {
            if (value.Ticks > TimestampOrigin.Ticks)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}={1}, ", name, value));
            }
        }

        /// <summary>
        /// A sorted specification de-duplicates and orders its entries, an unsorted one
        /// keeps insertion order.
        /// </summary>
        private ICollection<T> CreateCollection<T>()
        {
            return this.isSorted ? (ICollection<T>)new SortedSet<T>() : new List<T>();
        }
    }
}
