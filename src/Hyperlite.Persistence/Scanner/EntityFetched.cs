namespace Hyperlite.Persistence.Scanner
{
    using System;
    using Hyperlite;

    /// <param name="cell">
    ///     The fetched cell, if null then key, buffer, length must be provided.
    /// </param>
    /// <param name="key">
    ///     The cell key.
    /// </param>
    /// <param name="buffer">
    ///     The cell buffer.
    /// </param>
    /// <param name="length">
    ///     The cell buffer length.
    /// </param>
    internal delegate void EntityFetched(ICell cell, Key key, IntPtr buffer, int length, EntityScanTarget entityScanTarget);
}
