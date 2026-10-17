namespace Hyperlite
{
    /// <summary>
    /// A key and its value, as a scanner returns it: a <see cref="Cell" />, or a pooled or
    /// buffered cell when the scan was asked to reuse its instances.
    /// </summary>
    public interface ICell
    {
        /// <summary>
        /// Gets the cell key.
        /// </summary>
        /// <seealso cref="Hyperlite.Key" />
        Key Key { get; }

        /// <summary>
        /// Gets the cell value, might be null.
        /// </summary>
        byte[] Value { get; }

        /// <summary>
        /// Gets the cell value length.
        /// </summary>
        int ValueLength { get; }
    }
}
