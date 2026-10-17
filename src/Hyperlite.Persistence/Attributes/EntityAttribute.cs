namespace Hyperlite.Persistence.Attributes
{
    using System;

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class EntityAttribute : Attribute
    {
        public EntityAttribute()
        {
        }

        public EntityAttribute(string tableName)
        {
            this.TableName = tableName;
        }

        public string ColumnFamily { get; set; }

        public string ColumnQualifier { get; set; }

        /// <value>
        ///     The database namespace.
        /// </value>
        public string Namespace { get; set; }

        public string TableName { get; }
    }
}
