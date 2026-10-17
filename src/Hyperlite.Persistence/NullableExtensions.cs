namespace Hyperlite.Persistence
{
    internal static class NullableExtensions
    {
        /// <summary>
        ///     Gets a value indicating whether the value is false.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if value is <c>false</c>; otherwise <c>false</c>.
        /// </returns>
        public static bool IsFalse(this bool? value)
        {
            return value.HasValue && !value.Value;
        }

        /// <summary>
        ///     Gets a value indicating whether the value is null or false.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if value is <c>null</c> or <c>false</c>; otherwise <c>false</c>.
        /// </returns>
        public static bool IsNullOrFalse(this bool? value)
        {
            return !value.HasValue || !value.Value;
        }
    }
}
