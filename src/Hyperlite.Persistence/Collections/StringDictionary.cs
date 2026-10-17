namespace Hyperlite.Persistence.Collections
{
    using System.Collections.Generic;

    /// <summary>
    ///     The string dictionary.
    /// </summary>
    /// <typeparam name="T">
    ///     The value type.
    /// </typeparam>
    /// <typeparam name="TComparer">
    ///     The comparer type.
    /// </typeparam>
    internal class StringDictionary<T, TComparer> : Dictionary<string, T>
        where TComparer : struct, IEqualityComparer<string>
    {
        internal StringDictionary()
            : base(default(TComparer))
        {
        }
    }

    internal sealed class StringDictionary<T> : StringDictionary<T, StringComparer>
    {
        internal StringDictionary()
        {
        }
    }
}
