namespace Hyperlite.Persistence
{
    public interface ITableBinding
    {
        /// <value>
        ///     The database namespace.
        /// </value>
        string Namespace { get; }

        string TableName { get; }
    }
}
