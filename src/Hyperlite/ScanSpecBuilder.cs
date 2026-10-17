namespace Hyperlite.Fluent
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The terminal stage of a fluent scan specification: scalar options, then Build.
    /// </summary>
    public interface IScanSpecBuilderOp
    {
        /// <summary>Limits the number of rows returned.</summary>
        /// <param name="value">Row limit.</param>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp MaxRows(int value);

        /// <summary>Limits the number of cell versions returned.</summary>
        /// <param name="value">Version limit.</param>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp MaxVersions(int value);

        /// <summary>Limits the number of cells returned.</summary>
        /// <param name="value">Cell limit.</param>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp MaxCells(int value);

        /// <summary>Limits the number of cells returned per column family.</summary>
        /// <param name="value">Cell limit per column family.</param>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp MaxCellsPerColumnFamily(int value);

        /// <summary>Skips the given number of rows.</summary>
        /// <param name="value">Row offset.</param>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp RowOffset(int value);

        /// <summary>Skips the given number of cells.</summary>
        /// <param name="value">Cell offset.</param>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp CellOffset(int value);

        /// <summary>Sets the inclusive lower bound of the timestamp range.</summary>
        /// <param name="value">Lower bound.</param>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp StartDateTime(DateTime value);

        /// <summary>Sets the inclusive upper bound of the timestamp range.</summary>
        /// <param name="value">Upper bound.</param>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp EndDateTime(DateTime value);

        /// <summary>Filters rows by regular expression.</summary>
        /// <param name="value">Expression.</param>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp RowRegex(string value);

        /// <summary>Filters cell values by regular expression.</summary>
        /// <param name="value">Expression.</param>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp ValueRegex(string value);

        /// <summary>Looks the requested rows up one at a time.</summary>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp ScanAndFilter();

        /// <summary>Combines the column predicates with AND instead of OR.</summary>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp ColumnPredicateAnd();

        /// <summary>Returns keys without values.</summary>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp KeysOnly();

        /// <summary>Bypasses the query cache.</summary>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp NotUseQueryCache();

        /// <summary>Sets the scanner timeout.</summary>
        /// <param name="value">Timeout.</param>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp Timeout(TimeSpan value);

        /// <summary>Sets the scanner flags.</summary>
        /// <returns>This stage.</returns>
        IScanSpecBuilderOp Flags(ScannerFlags value);

        /// <summary>Completes the specification.</summary>
        /// <returns>The scan specification.</returns>
        ScanSpec Build();
    }

    /// <summary>
    /// The row and column predicate stage of a fluent scan specification.
    /// </summary>
    public interface IScanSpecBuilderWithRow
    {
        /// <summary>Continues without adding anything.</summary>
        /// <returns>The row stage.</returns>
        IScanSpecBuilderWithRowOp WithRows();

        /// <summary>Adds rows.</summary>
        /// <param name="row">Row key, mandatory.</param>
        /// <param name="moreRows">Further row keys.</param>
        /// <returns>The row stage.</returns>
        IScanSpecBuilderWithRowOp WithRows(string row, params string[] moreRows);

        /// <summary>Adds rows.</summary>
        /// <param name="rows">Row keys.</param>
        /// <returns>The row stage.</returns>
        IScanSpecBuilderWithRowOp WithRows(IEnumerable<string> rows);

        /// <summary>Adds row intervals.</summary>
        /// <param name="rowInterval">Row interval, mandatory.</param>
        /// <param name="moreRowIntervals">Further row intervals.</param>
        /// <returns>The row stage.</returns>
        IScanSpecBuilderWithRowOp WithRows(RowInterval rowInterval, params RowInterval[] moreRowIntervals);

        /// <summary>Adds row intervals.</summary>
        /// <returns>The row stage.</returns>
        IScanSpecBuilderWithRowOp WithRows(IEnumerable<RowInterval> rowIntervals);

        /// <summary>Adds column predicates.</summary>
        /// <param name="columnPredicate">Column predicate, mandatory.</param>
        /// <param name="moreColumnPredicates">Further column predicates.</param>
        /// <returns>The row stage.</returns>
        IScanSpecBuilderWithRowOp WithColumnPredicates(
            ColumnPredicate columnPredicate,
            params ColumnPredicate[] moreColumnPredicates);

        /// <summary>Adds column predicates.</summary>
        /// <returns>The row stage.</returns>
        IScanSpecBuilderWithRowOp WithColumnPredicates(IEnumerable<ColumnPredicate> columnPredicates);
    }

    /// <summary>
    /// The row stage, which can also finish the specification.
    /// </summary>
    public interface IScanSpecBuilderWithRowOp : IScanSpecBuilderWithRow, IScanSpecBuilderOp
    {
    }

    /// <summary>
    /// The cell stage of a fluent scan specification.
    /// </summary>
    public interface IScanSpecBuilderWithCell
    {
        /// <summary>Adds a cell.</summary>
        /// <param name="row">Row key, mandatory.</param>
        /// <param name="columnFamily">Column family, mandatory.</param>
        /// <param name="columnQualifier">Column qualifier, might be null.</param>
        /// <returns>The cell stage.</returns>
        IScanSpecBuilderWithCellOp WithCells(string row, string columnFamily, string columnQualifier);

        /// <summary>Adds cells.</summary>
        /// <param name="key">Cell key, mandatory.</param>
        /// <param name="moreKeys">Further cell keys.</param>
        /// <returns>The cell stage.</returns>
        IScanSpecBuilderWithCellOp WithCells(Key key, params Key[] moreKeys);

        /// <summary>Adds cells.</summary>
        /// <param name="keys">Cell keys.</param>
        /// <returns>The cell stage.</returns>
        IScanSpecBuilderWithCellOp WithCells(IEnumerable<Key> keys);

        /// <summary>Adds cell intervals.</summary>
        /// <param name="cellInterval">Cell interval, mandatory.</param>
        /// <param name="moreCellIntervals">Further cell intervals.</param>
        /// <returns>The cell stage.</returns>
        IScanSpecBuilderWithCellOp WithCells(
            CellInterval cellInterval,
            params CellInterval[] moreCellIntervals);

        /// <summary>Adds cell intervals.</summary>
        /// <returns>The cell stage.</returns>
        IScanSpecBuilderWithCellOp WithCells(IEnumerable<CellInterval> cellIntervals);
    }

    /// <summary>
    /// The cell stage, which can also finish the specification.
    /// </summary>
    public interface IScanSpecBuilderWithCellOp : IScanSpecBuilderWithCell, IScanSpecBuilderOp
    {
    }

    /// <summary>
    /// The stage after the columns: either rows or cells, not both.
    /// </summary>
    public interface IScanSpecBuilderWhere : IScanSpecBuilderWithRow, IScanSpecBuilderWithCell
    {
    }

    /// <summary>
    /// The first stage of a fluent scan specification.
    /// </summary>
    public interface IScanSpecBuilderColumns
    {
        /// <summary>Continues without restricting the columns.</summary>
        /// <returns>The next stage.</returns>
        IScanSpecBuilderWhere WithColumns();

        /// <summary>Restricts the scan to the given columns.</summary>
        /// <param name="column">Column, mandatory.</param>
        /// <param name="moreColumns">Further columns.</param>
        /// <returns>The next stage.</returns>
        IScanSpecBuilderWhere WithColumns(string column, params string[] moreColumns);

        /// <summary>Restricts the scan to the given columns.</summary>
        /// <returns>The next stage.</returns>
        IScanSpecBuilderWhere WithColumns(IEnumerable<string> columns);
    }
}

namespace Hyperlite
{
    using System;
    using System.Collections.Generic;

    using Hyperlite.Fluent;

    /// <summary>
    /// Builds a <see cref="ScanSpec" /> fluently.
    /// </summary>
    /// <remarks>
    /// The stages exist to make an invalid specification unrepresentable: columns come
    /// first, then either rows or cells - never both, since hypertable rejects that
    /// combination - and only then the scalar options.
    /// </remarks>
    /// <example>
    /// <code>
    /// var spec = ScanSpecBuilder.Create()
    ///     .WithColumns("a", "b")
    ///     .WithRows("r1", "r2")
    ///     .MaxRows(10)
    ///     .KeysOnly()
    ///     .Build();
    /// </code>
    /// </example>
    public static class ScanSpecBuilder
    {
        /// <summary>
        /// Starts a specification that keeps its rows, cells and intervals in insertion
        /// order.
        /// </summary>
        /// <returns>The first stage.</returns>
        public static IScanSpecBuilderColumns Create()
        {
            return new Columns(new ScanSpec(false));
        }

        /// <summary>
        /// Starts a specification that keeps its rows, cells and intervals sorted and
        /// de-duplicated.
        /// </summary>
        /// <returns>The first stage.</returns>
        public static IScanSpecBuilderColumns CreateOrdered()
        {
            return new Columns(new ScanSpec(true));
        }

        /// <summary>
        /// Common state: every stage decorates the same specification instance.
        /// </summary>
        private abstract class StageBase
        {
            protected StageBase(ScanSpec scanSpec)
            {
                this.ScanSpec = scanSpec;
            }

            protected ScanSpec ScanSpec { get; private set; }
        }

        /// <summary>
        /// The scalar options, shared by the row and the cell stage.
        /// </summary>
        private abstract class OpStage : StageBase, IScanSpecBuilderOp
        {
            protected OpStage(ScanSpec scanSpec)
                : base(scanSpec)
            {
            }

            public IScanSpecBuilderOp MaxRows(int value)
            {
                this.ScanSpec.MaxRows = value;
                return this;
            }

            public IScanSpecBuilderOp MaxVersions(int value)
            {
                this.ScanSpec.MaxVersions = value;
                return this;
            }

            public IScanSpecBuilderOp MaxCells(int value)
            {
                this.ScanSpec.MaxCells = value;
                return this;
            }

            public IScanSpecBuilderOp MaxCellsPerColumnFamily(int value)
            {
                this.ScanSpec.MaxCellsColumnFamily = value;
                return this;
            }

            public IScanSpecBuilderOp RowOffset(int value)
            {
                this.ScanSpec.RowOffset = value;
                return this;
            }

            public IScanSpecBuilderOp CellOffset(int value)
            {
                this.ScanSpec.CellOffset = value;
                return this;
            }

            public IScanSpecBuilderOp StartDateTime(DateTime value)
            {
                this.ScanSpec.StartDateTime = value;
                return this;
            }

            public IScanSpecBuilderOp EndDateTime(DateTime value)
            {
                this.ScanSpec.EndDateTime = value;
                return this;
            }

            public IScanSpecBuilderOp RowRegex(string value)
            {
                this.ScanSpec.RowRegex = value;
                return this;
            }

            public IScanSpecBuilderOp ValueRegex(string value)
            {
                this.ScanSpec.ValueRegex = value;
                return this;
            }

            public IScanSpecBuilderOp ScanAndFilter()
            {
                this.ScanSpec.ScanAndFilter = true;
                return this;
            }

            public IScanSpecBuilderOp ColumnPredicateAnd()
            {
                this.ScanSpec.ColumnPredicateAnd = true;
                return this;
            }

            public IScanSpecBuilderOp KeysOnly()
            {
                this.ScanSpec.KeysOnly = true;
                return this;
            }

            public IScanSpecBuilderOp NotUseQueryCache()
            {
                this.ScanSpec.NotUseQueryCache = true;
                return this;
            }

            public IScanSpecBuilderOp Timeout(TimeSpan value)
            {
                this.ScanSpec.Timeout = value;
                return this;
            }

            public IScanSpecBuilderOp Flags(ScannerFlags value)
            {
                this.ScanSpec.Flags = value;
                return this;
            }

            public ScanSpec Build()
            {
                return this.ScanSpec;
            }
        }

        private sealed class WithRowOp : OpStage, IScanSpecBuilderWithRowOp
        {
            internal WithRowOp(ScanSpec scanSpec)
                : base(scanSpec)
            {
            }

            public IScanSpecBuilderWithRowOp WithRows()
            {
                return this;
            }

            public IScanSpecBuilderWithRowOp WithRows(string row, params string[] moreRows)
            {
                this.ScanSpec.AddRow(row, moreRows);
                return this;
            }

            public IScanSpecBuilderWithRowOp WithRows(IEnumerable<string> rows)
            {
                this.ScanSpec.AddRow(rows);
                return this;
            }

            public IScanSpecBuilderWithRowOp WithRows(
                RowInterval rowInterval,
                params RowInterval[] moreRowIntervals)
            {
                this.ScanSpec.AddRowInterval(rowInterval, moreRowIntervals);
                return this;
            }

            public IScanSpecBuilderWithRowOp WithRows(IEnumerable<RowInterval> rowIntervals)
            {
                this.ScanSpec.AddRowInterval(rowIntervals);
                return this;
            }

            public IScanSpecBuilderWithRowOp WithColumnPredicates(
                ColumnPredicate columnPredicate,
                params ColumnPredicate[] moreColumnPredicates)
            {
                this.ScanSpec.AddColumnPredicate(columnPredicate, moreColumnPredicates);
                return this;
            }

            public IScanSpecBuilderWithRowOp WithColumnPredicates(
                IEnumerable<ColumnPredicate> columnPredicates)
            {
                this.ScanSpec.AddColumnPredicate(columnPredicates);
                return this;
            }
        }

        private sealed class WithCellOp : OpStage, IScanSpecBuilderWithCellOp
        {
            internal WithCellOp(ScanSpec scanSpec)
                : base(scanSpec)
            {
            }

            public IScanSpecBuilderWithCellOp WithCells(
                string row,
                string columnFamily,
                string columnQualifier)
            {
                this.ScanSpec.AddCell(row, columnFamily, columnQualifier);
                return this;
            }

            public IScanSpecBuilderWithCellOp WithCells(Key key, params Key[] moreKeys)
            {
                this.ScanSpec.AddCell(key, moreKeys);
                return this;
            }

            public IScanSpecBuilderWithCellOp WithCells(IEnumerable<Key> keys)
            {
                this.ScanSpec.AddCell(keys);
                return this;
            }

            public IScanSpecBuilderWithCellOp WithCells(
                CellInterval cellInterval,
                params CellInterval[] moreCellIntervals)
            {
                this.ScanSpec.AddCellInterval(cellInterval, moreCellIntervals);
                return this;
            }

            public IScanSpecBuilderWithCellOp WithCells(IEnumerable<CellInterval> cellIntervals)
            {
                this.ScanSpec.AddCellInterval(cellIntervals);
                return this;
            }
        }

        /// <summary>
        /// After the columns: the first row or cell call decides which branch the
        /// specification takes.
        /// </summary>
        private sealed class Where : StageBase, IScanSpecBuilderWhere
        {
            internal Where(ScanSpec scanSpec)
                : base(scanSpec)
            {
            }

            public IScanSpecBuilderWithRowOp WithRows()
            {
                return new WithRowOp(this.ScanSpec);
            }

            public IScanSpecBuilderWithRowOp WithRows(string row, params string[] moreRows)
            {
                this.ScanSpec.AddRow(row, moreRows);
                return new WithRowOp(this.ScanSpec);
            }

            public IScanSpecBuilderWithRowOp WithRows(IEnumerable<string> rows)
            {
                this.ScanSpec.AddRow(rows);
                return new WithRowOp(this.ScanSpec);
            }

            public IScanSpecBuilderWithRowOp WithRows(
                RowInterval rowInterval,
                params RowInterval[] moreRowIntervals)
            {
                this.ScanSpec.AddRowInterval(rowInterval, moreRowIntervals);
                return new WithRowOp(this.ScanSpec);
            }

            public IScanSpecBuilderWithRowOp WithRows(IEnumerable<RowInterval> rowIntervals)
            {
                this.ScanSpec.AddRowInterval(rowIntervals);
                return new WithRowOp(this.ScanSpec);
            }

            public IScanSpecBuilderWithRowOp WithColumnPredicates(
                ColumnPredicate columnPredicate,
                params ColumnPredicate[] moreColumnPredicates)
            {
                this.ScanSpec.AddColumnPredicate(columnPredicate, moreColumnPredicates);
                return new WithRowOp(this.ScanSpec);
            }

            public IScanSpecBuilderWithRowOp WithColumnPredicates(
                IEnumerable<ColumnPredicate> columnPredicates)
            {
                this.ScanSpec.AddColumnPredicate(columnPredicates);
                return new WithRowOp(this.ScanSpec);
            }

            public IScanSpecBuilderWithCellOp WithCells(
                string row,
                string columnFamily,
                string columnQualifier)
            {
                this.ScanSpec.AddCell(row, columnFamily, columnQualifier);
                return new WithCellOp(this.ScanSpec);
            }

            public IScanSpecBuilderWithCellOp WithCells(Key key, params Key[] moreKeys)
            {
                this.ScanSpec.AddCell(key, moreKeys);
                return new WithCellOp(this.ScanSpec);
            }

            public IScanSpecBuilderWithCellOp WithCells(IEnumerable<Key> keys)
            {
                this.ScanSpec.AddCell(keys);
                return new WithCellOp(this.ScanSpec);
            }

            public IScanSpecBuilderWithCellOp WithCells(
                CellInterval cellInterval,
                params CellInterval[] moreCellIntervals)
            {
                this.ScanSpec.AddCellInterval(cellInterval, moreCellIntervals);
                return new WithCellOp(this.ScanSpec);
            }

            public IScanSpecBuilderWithCellOp WithCells(IEnumerable<CellInterval> cellIntervals)
            {
                this.ScanSpec.AddCellInterval(cellIntervals);
                return new WithCellOp(this.ScanSpec);
            }
        }

        private sealed class Columns : StageBase, IScanSpecBuilderColumns
        {
            internal Columns(ScanSpec scanSpec)
                : base(scanSpec)
            {
            }

            public IScanSpecBuilderWhere WithColumns()
            {
                return new Where(this.ScanSpec);
            }

            public IScanSpecBuilderWhere WithColumns(string column, params string[] moreColumns)
            {
                this.ScanSpec.AddColumn(column, moreColumns);
                return new Where(this.ScanSpec);
            }

            public IScanSpecBuilderWhere WithColumns(IEnumerable<string> columns)
            {
                this.ScanSpec.AddColumn(columns);
                return new Where(this.ScanSpec);
            }
        }
    }
}
