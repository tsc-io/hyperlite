namespace Hyperlite.Persistence
{
    public interface IColumnBinding
    {
        string ColumnFamily { get; }

        string ColumnQualifier { get; }
    }
}
