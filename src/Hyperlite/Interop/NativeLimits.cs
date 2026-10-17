namespace Hyperlite.Interop
{
    /// <summary>
    /// Limits the native layer imposes. Kept in step with Hyperlite.Native by hand, which
    /// is safe because they are compile time constants there too.
    /// </summary>
    internal static class NativeLimits
    {
        /// <summary>
        /// Maximum cell size, Hyperlite::k_max_cell_size and SQLITE_MAX_LENGTH.
        /// </summary>
        internal const int MaxCellSize = 2000000000;
    }
}
