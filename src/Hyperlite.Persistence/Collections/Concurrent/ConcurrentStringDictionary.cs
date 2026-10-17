namespace Hyperlite.Persistence.Collections.Concurrent
{
    /// <summary>
    ///     A dictionary keyed on a string the library chooses itself, that several threads
    ///     may use at once.
    /// </summary>
    /// <typeparam name="T">
    ///     The value type.
    /// </typeparam>
    /// <remarks>
    ///     A copy-on-write flat table, for the reason <see cref="ConcurrentTypeDictionary{TValue}" />
    ///     is one: each of these is written once per name - a table, a namespace, a mutator, a
    ///     regular expression - and read from then on.
    ///     <para>
    ///     Keyed on <see cref="InternalStringComparer" />, the runtime's non-randomized string
    ///     hash. With the randomized one a flat table measured 60-80% slower than
    ///     <c>ConcurrentDictionary</c> on .NET 10; with this one it is 45% faster on net48, 29%
    ///     faster on .NET 8 and level on .NET 10.
    ///     </para>
    ///     <para>
    ///     <b>Never key one on a string that arrives from outside</b> - a non-randomized hash
    ///     can be flooded. <c>Reflection.TypeLoader</c>, whose keys are type names read from
    ///     serialized bytes, uses a framework dictionary for that reason.
    ///     </para>
    /// </remarks>
    internal sealed class ConcurrentStringDictionary<T> : ConcurrentFlatDictionary<string, T, InternalStringComparer>
    {
        internal ConcurrentStringDictionary()
        {
        }
    }
}
