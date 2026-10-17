namespace Hyperlite.Persistence.Serialization
{
    using System;

    //// TODO any other type required? XmlDocument, XmlNode, Tuple, etc.
    //// TODO organize better?

    [Flags]
    public enum Tags
    {
        Null = 0,

        Array = 1,

        FirstDecoderTag = 1 << 1,

        True = 1 << 1,

        False = 2 << 1,

        Bool = 3 << 1,

        SByte = 4 << 1,

        /// <summary>
        ///     The s byte 0.
        /// </summary>
        SByte0 = 5 << 1,

        Byte = 6 << 1,

        /// <summary>
        ///     The byte 0.
        /// </summary>
        Byte0 = 7 << 1,

        Short = 8 << 1,

        /// <summary>
        ///     The short 0.
        /// </summary>
        Short0 = 9 << 1,

        UShort = 10 << 1,

        /// <summary>
        ///     The u short 0.
        /// </summary>
        UShort0 = 11 << 1,

        Int = 12 << 1,

        /// <summary>
        ///     The int 0.
        /// </summary>
        Int0 = 13 << 1,

        UInt = 14 << 1,

        /// <summary>
        ///     The u int 0.
        /// </summary>
        UInt0 = 15 << 1,

        Long = 16 << 1,

        /// <summary>
        ///     The long 0.
        /// </summary>
        Long0 = 17 << 1,

        ULong = 18 << 1,

        /// <summary>
        ///     The u long 0.
        /// </summary>
        ULong0 = 19 << 1,

        Char = 20 << 1,

        Float = 21 << 1,

        /// <summary>
        ///     The float 0.
        /// </summary>
        Float0 = 22 << 1,

        FloatNaN = 23 << 1,

        Double = 24 << 1,

        /// <summary>
        ///     The double 0.
        /// </summary>
        Double0 = 25 << 1,

        DoubleNaN = 26 << 1,

        Decimal = 27 << 1,

        /// <summary>
        ///     The date time 0.
        /// </summary>
        DateTime0 = 28 << 1,

        DateTime = 29 << 1,

        /// <summary>
        ///     The date time offset 0.
        /// </summary>
        DateTimeOffset0 = 30 << 1,

        DateTimeOffset = 31 << 1,

        TimeSpan = 32 << 1,

        /// <summary>
        ///     The time span 0.
        /// </summary>
        TimeSpan0 = 33 << 1,

        String = 34 << 1,

        StringEmpty = 35 << 1,

        Guid = 36 << 1,

        Type = 37 << 1,

        TypeCode = 38 << 1,

        Uri = 39 << 1,

        LastDecoderTag = Uri,

        Tuple = 48 << 1,

        KeyValuePair = 49 << 1,

        Collection = 50 << 1,

        Enumerable = 51 << 1,

        Dictionary = 52 << 1,

        SerializationInfo = 53 << 1,

        Object = 54 << 1,

        ObjectRef = 55 << 1,

        TypeSchema = 56 << 1,

        /// <summary>
        ///     The new type schema, introduced with > v0.9.8.
        /// </summary>
        TypeSchema2 = TypeSchema | Array,

        TypeSchemaRef = 57 << 1,

        TypeRef = 58 << 1,

        StringRef = 59 << 1,

        EntityRef = 60 << 1,

        EntityKey = 61 << 1,

        EntityRow = 62 << 1,

        /// <summary>
        ///     End of list.
        /// </summary>
        End = 63 << 1,

        FirstCustomType = 128
    }
}
