namespace Hyperlite.Persistence.Test.Serialization
{
    using System.Globalization;

    /// <summary>
    ///     The culture the serialization fixtures run under.
    /// </summary>
    /// <remarks>
    ///     Several cases write a value of one type and read it back as another, and that
    ///     conversion goes through the value's string form - a double read as a string, or a
    ///     string read as a double. Under a culture whose decimal separator is a comma those
    ///     cases would compare a different number, so the fixtures pin the invariant culture.
    ///     <para>
    ///     Each fixture calls this from its own ClassInitialize rather than inheriting one:
    ///     MSTest runs a base class's ClassInitialize once for the base, not once per derived
    ///     fixture, unless it is told to, and a one-line call is clearer than that attribute.
    ///     </para>
    /// </remarks>
    internal static class InvariantCulture
    {
        internal static void Pin()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }
    }
}
