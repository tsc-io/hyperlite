namespace Hyperlite.Persistence.Bindings
{
    using System;

    public sealed class ColumnBinding : IColumnBinding
    {
        private readonly string columnFamily;

        private readonly string columnQualifier;

        /// <exception cref="ArgumentNullException">
        ///     If the <paramref name="columnFamily" /> is null or empty.
        /// </exception>
        public ColumnBinding(string columnFamily)
            : this(columnFamily, null)
        {
        }

        /// <exception cref="ArgumentNullException">
        ///     If the <paramref name="columnFamily" /> is null or empty.
        /// </exception>
        public ColumnBinding(string columnFamily, string columnQualifier)
        {
            if (string.IsNullOrEmpty(columnFamily))
            {
                throw new ArgumentNullException(nameof(columnFamily));
            }

            this.columnFamily = columnFamily;
            this.columnQualifier = columnQualifier;
        }

        public string ColumnFamily => this.columnFamily;

        public string ColumnQualifier => this.columnQualifier;
    }
}
