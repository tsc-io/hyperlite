namespace Hyperlite
{
    using System;
    using System.Runtime.Serialization;

    /// <summary>
    /// Represents the base class for all Hyperlite exceptions.
    /// </summary>
    [Serializable]
    public class HyperliteException : Exception
    {
        private readonly int code;

        private readonly int line;

        private readonly string func;

        private readonly string file;

        internal HyperliteException()
        {
            this.func = string.Empty;
            this.file = string.Empty;
        }

        internal HyperliteException(string message)
            : base(message)
        {
            this.func = string.Empty;
            this.file = string.Empty;
        }

        internal HyperliteException(string message, Exception innerException)
            : base(message, innerException)
        {
            this.func = string.Empty;
            this.file = string.Empty;
        }

        internal HyperliteException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(message, innerException)
        {
            this.code = errorCode;
            this.line = errorLine;
            this.func = func ?? string.Empty;
            this.file = file ?? string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HyperliteException" /> class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected HyperliteException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            if (info != null)
            {
                this.code = info.GetInt32("code");
                this.line = info.GetInt32("line");
                this.func = info.GetString("func");
                this.file = info.GetString("file");
            }
        }

        /// <summary>
        /// Gets the internal error code.
        /// </summary>
        public int ErrorCode
        {
            get
            {
                return this.code;
            }
        }

        /// <summary>
        /// Gets the source code line where the error occurred.
        /// </summary>
        public int ErrorLine
        {
            get
            {
                return this.line;
            }
        }

        /// <summary>
        /// Gets the function or method where the error occurred.
        /// </summary>
        public string Func
        {
            get
            {
                return this.func;
            }
        }

        /// <summary>
        /// Gets the source code file name where the error occurred.
        /// </summary>
        public string File
        {
            get
            {
                return this.file;
            }
        }

        /// <summary>
        /// Sets the SerializationInfo with additional information about this exception instance.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info != null)
            {
                info.AddValue("code", this.code);
                info.AddValue("line", this.line);
                info.AddValue("func", this.func);
                info.AddValue("file", this.file);
            }

            base.GetObjectData(info, context);
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a protocol failure.
    /// </summary>
    [Serializable]
    public sealed class ProtocolException : HyperliteException
    {
        internal ProtocolException(string message)
            : base(message)
        {
        }

        internal ProtocolException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private ProtocolException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a truncated request.
    /// </summary>
    [Serializable]
    public sealed class RequestTruncatedException : HyperliteException
    {
        internal RequestTruncatedException(string message)
            : base(message)
        {
        }

        internal RequestTruncatedException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private RequestTruncatedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a truncated response.
    /// </summary>
    [Serializable]
    public sealed class ResponseTruncatedException : HyperliteException
    {
        internal ResponseTruncatedException(string message)
            : base(message)
        {
        }

        internal ResponseTruncatedException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private ResponseTruncatedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a request timeout.
    /// </summary>
    [Serializable]
    public sealed class TimeoutException : HyperliteException
    {
        internal TimeoutException(string message)
            : base(message)
        {
        }

        internal TimeoutException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private TimeoutException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a local i/o failure.
    /// </summary>
    [Serializable]
    public sealed class IoException : HyperliteException
    {
        internal IoException(string message)
            : base(message)
        {
        }

        internal IoException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private IoException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a bad root location.
    /// </summary>
    [Serializable]
    public sealed class BadRootLocationException : HyperliteException
    {
        internal BadRootLocationException(string message)
            : base(message)
        {
        }

        internal BadRootLocationException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private BadRootLocationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a bad schema.
    /// </summary>
    [Serializable]
    public sealed class BadSchemaException : HyperliteException
    {
        internal BadSchemaException(string message)
            : base(message)
        {
        }

        internal BadSchemaException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private BadSchemaException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of invalid metadata.
    /// </summary>
    [Serializable]
    public sealed class InvalidMetadataException : HyperliteException
    {
        internal InvalidMetadataException(string message)
            : base(message)
        {
        }

        internal InvalidMetadataException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private InvalidMetadataException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a bad key.
    /// </summary>
    [Serializable]
    public sealed class BadKeyException : HyperliteException
    {
        internal BadKeyException(string message)
            : base(message)
        {
        }

        internal BadKeyException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private BadKeyException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of missing metadata.
    /// </summary>
    [Serializable]
    public sealed class MetadataNotFoundException : HyperliteException
    {
        internal MetadataNotFoundException(string message)
            : base(message)
        {
        }

        internal MetadataNotFoundException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private MetadataNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a missing file.
    /// </summary>
    [Serializable]
    public sealed class FileNotFoundException : HyperliteException
    {
        internal FileNotFoundException(string message)
            : base(message)
        {
        }

        internal FileNotFoundException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private FileNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a missing table.
    /// </summary>
    [Serializable]
    public sealed class TableNotFoundException : HyperliteException
    {
        internal TableNotFoundException(string message)
            : base(message)
        {
        }

        internal TableNotFoundException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private TableNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a malformed request.
    /// </summary>
    [Serializable]
    public sealed class MalformedRequestException : HyperliteException
    {
        internal MalformedRequestException(string message)
            : base(message)
        {
        }

        internal MalformedRequestException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private MalformedRequestException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of too many columns.
    /// </summary>
    [Serializable]
    public sealed class TooManyColumnsException : HyperliteException
    {
        internal TooManyColumnsException(string message)
            : base(message)
        {
        }

        internal TooManyColumnsException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private TooManyColumnsException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a bad domain name.
    /// </summary>
    [Serializable]
    public sealed class BadDomainNameException : HyperliteException
    {
        internal BadDomainNameException(string message)
            : base(message)
        {
        }

        internal BadDomainNameException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private BadDomainNameException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a command parse failure.
    /// </summary>
    [Serializable]
    public sealed class CommandParseException : HyperliteException
    {
        internal CommandParseException(string message)
            : base(message)
        {
        }

        internal CommandParseException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private CommandParseException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a failure connecting to the master.
    /// </summary>
    [Serializable]
    public sealed class ConnectMasterException : HyperliteException
    {
        internal ConnectMasterException(string message)
            : base(message)
        {
        }

        internal ConnectMasterException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private ConnectMasterException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a failure connecting to hyperspace.
    /// </summary>
    [Serializable]
    public sealed class ConnectHyperspaceException : HyperliteException
    {
        internal ConnectHyperspaceException(string message)
            : base(message)
        {
        }

        internal ConnectHyperspaceException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private ConnectHyperspaceException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a failed memory allocation.
    /// </summary>
    [Serializable]
    public sealed class BadMemoryAllocationException : HyperliteException
    {
        internal BadMemoryAllocationException(string message)
            : base(message)
        {
        }

        internal BadMemoryAllocationException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private BadMemoryAllocationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a bad scan specification.
    /// </summary>
    [Serializable]
    public sealed class BadScanSpecException : HyperliteException
    {
        internal BadScanSpecException(string message)
            : base(message)
        {
        }

        internal BadScanSpecException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private BadScanSpecException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a version mismatch.
    /// </summary>
    [Serializable]
    public sealed class VersionMismatchException : HyperliteException
    {
        internal VersionMismatchException(string message)
            : base(message)
        {
        }

        internal VersionMismatchException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private VersionMismatchException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a cancelled operation.
    /// </summary>
    [Serializable]
    public sealed class CancelledException : HyperliteException
    {
        internal CancelledException(string message)
            : base(message)
        {
        }

        internal CancelledException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private CancelledException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a schema parse failure.
    /// </summary>
    [Serializable]
    public sealed class SchemaParseException : HyperliteException
    {
        internal SchemaParseException(string message)
            : base(message)
        {
        }

        internal SchemaParseException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private SchemaParseException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a syntax error.
    /// </summary>
    [Serializable]
    public sealed class SyntaxException : HyperliteException
    {
        internal SyntaxException(string message)
            : base(message)
        {
        }

        internal SyntaxException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private SyntaxException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a double unget.
    /// </summary>
    [Serializable]
    public sealed class DoubleUngetException : HyperliteException
    {
        internal DoubleUngetException(string message)
            : base(message)
        {
        }

        internal DoubleUngetException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private DoubleUngetException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of an empty bloom filter.
    /// </summary>
    [Serializable]
    public sealed class EmptyBloomFilterException : HyperliteException
    {
        internal EmptyBloomFilterException(string message)
            : base(message)
        {
        }

        internal EmptyBloomFilterException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private EmptyBloomFilterException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a bloom filter checksum mismatch.
    /// </summary>
    [Serializable]
    public sealed class BloomFilterChecksumMismatchException : HyperliteException
    {
        internal BloomFilterChecksumMismatchException(string message)
            : base(message)
        {
        }

        internal BloomFilterChecksumMismatchException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private BloomFilterChecksumMismatchException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a name which is already in use.
    /// </summary>
    [Serializable]
    public sealed class NameAlreadyInUseException : HyperliteException
    {
        internal NameAlreadyInUseException(string message)
            : base(message)
        {
        }

        internal NameAlreadyInUseException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private NameAlreadyInUseException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a missing namespace.
    /// </summary>
    [Serializable]
    public sealed class NamespaceDoesNotExistException : HyperliteException
    {
        internal NamespaceDoesNotExistException(string message)
            : base(message)
        {
        }

        internal NamespaceDoesNotExistException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private NamespaceDoesNotExistException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a bad namespace.
    /// </summary>
    [Serializable]
    public sealed class BadNamespaceException : HyperliteException
    {
        internal BadNamespaceException(string message)
            : base(message)
        {
        }

        internal BadNamespaceException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private BadNamespaceException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a namespace which already exists.
    /// </summary>
    [Serializable]
    public sealed class NamespaceExistsException : HyperliteException
    {
        internal NamespaceExistsException(string message)
            : base(message)
        {
        }

        internal NamespaceExistsException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private NamespaceExistsException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a missing response.
    /// </summary>
    [Serializable]
    public sealed class NoResponseException : HyperliteException
    {
        internal NoResponseException(string message)
            : base(message)
        {
        }

        internal NoResponseException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private NoResponseException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a disallowed operation.
    /// </summary>
    [Serializable]
    public sealed class NotAllowedException : HyperliteException
    {
        internal NotAllowedException(string message)
            : base(message)
        {
        }

        internal NotAllowedException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private NotAllowedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of an induced failure.
    /// </summary>
    [Serializable]
    public sealed class InducedFailureException : HyperliteException
    {
        internal InducedFailureException(string message)
            : base(message)
        {
        }

        internal InducedFailureException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private InducedFailureException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a server which is shutting down.
    /// </summary>
    [Serializable]
    public sealed class ServerShuttingDownException : HyperliteException
    {
        internal ServerShuttingDownException(string message)
            : base(message)
        {
        }

        internal ServerShuttingDownException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private ServerShuttingDownException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of an unassigned location.
    /// </summary>
    [Serializable]
    public sealed class LocationUnassignedException : HyperliteException
    {
        internal LocationUnassignedException(string message)
            : base(message)
        {
        }

        internal LocationUnassignedException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private LocationUnassignedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of an object which already exists.
    /// </summary>
    [Serializable]
    public sealed class AlreadyExistsException : HyperliteException
    {
        internal AlreadyExistsException(string message)
            : base(message)
        {
        }

        internal AlreadyExistsException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private AlreadyExistsException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a checksum mismatch.
    /// </summary>
    [Serializable]
    public sealed class ChecksumMismatchException : HyperliteException
    {
        internal ChecksumMismatchException(string message)
            : base(message)
        {
        }

        internal ChecksumMismatchException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private ChecksumMismatchException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of an operation on something already closed.
    /// </summary>
    [Serializable]
    public sealed class ClosedException : HyperliteException
    {
        internal ClosedException(string message)
            : base(message)
        {
        }

        internal ClosedException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private ClosedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a missing range server.
    /// </summary>
    [Serializable]
    public sealed class RangeServerNotFoundException : HyperliteException
    {
        internal RangeServerNotFoundException(string message)
            : base(message)
        {
        }

        internal RangeServerNotFoundException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private RangeServerNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of an uninitialized connection.
    /// </summary>
    [Serializable]
    public sealed class ConnectionNotInitializedException : HyperliteException
    {
        internal ConnectionNotInitializedException(string message)
            : base(message)
        {
        }

        internal ConnectionNotInitializedException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private ConnectionNotInitializedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a table which already exists.
    /// </summary>
    [Serializable]
    public sealed class TableExistsException : HyperliteException
    {
        internal TableExistsException(string message)
            : base(message)
        {
        }

        internal TableExistsException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private TableExistsException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a configuration failure.
    /// </summary>
    [Serializable]
    public sealed class ConfigException : HyperliteException
    {
        internal ConfigException(string message)
            : base(message)
        {
        }

        internal ConfigException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private ConfigException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a communication failure.
    /// </summary>
    [Serializable]
    public sealed class CommException : HyperliteException
    {
        internal CommException(string message)
            : base(message)
        {
        }

        internal CommException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private CommException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a distributed filesystem broker failure.
    /// </summary>
    [Serializable]
    public sealed class DfsBrokerException : HyperliteException
    {
        internal DfsBrokerException(string message)
            : base(message)
        {
        }

        internal DfsBrokerException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private DfsBrokerException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a hyperspace failure.
    /// </summary>
    [Serializable]
    public sealed class HyperspaceException : HyperliteException
    {
        internal HyperspaceException(string message)
            : base(message)
        {
        }

        internal HyperspaceException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private HyperspaceException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a master failure.
    /// </summary>
    [Serializable]
    public sealed class MasterException : HyperliteException
    {
        internal MasterException(string message)
            : base(message)
        {
        }

        internal MasterException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private MasterException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a range server failure.
    /// </summary>
    [Serializable]
    public sealed class RangeServerException : HyperliteException
    {
        internal RangeServerException(string message)
            : base(message)
        {
        }

        internal RangeServerException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private RangeServerException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a metalog failure.
    /// </summary>
    [Serializable]
    public sealed class MetaLogException : HyperliteException
    {
        internal MetaLogException(string message)
            : base(message)
        {
        }

        internal MetaLogException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private MetaLogException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a serialization failure.
    /// </summary>
    [Serializable]
    public sealed class SerializationException : HyperliteException
    {
        internal SerializationException(string message)
            : base(message)
        {
        }

        internal SerializationException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private SerializationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown as the result of a thrift broker failure.
    /// </summary>
    [Serializable]
    public sealed class ThriftBrokerException : HyperliteException
    {
        internal ThriftBrokerException(string message)
            : base(message)
        {
        }

        internal ThriftBrokerException(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
            : base(errorCode, message, errorLine, func, file, innerException)
        {
        }

        private ThriftBrokerException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Maps a native error code onto the exception type ht4n raised for it.
    /// </summary>
    /// <remarks>
    /// The mapping is the one in ht4n's HypertableException::Create and is the reason
    /// Hyperlite needs no error classification in native code: the raw code decides.
    /// Consumers catch these types by name, so neither the names nor the codes may
    /// drift.
    /// </remarks>
    internal static class HyperliteExceptionFactory
    {
        /// <summary>
        /// Creates the exception for the given native failure.
        /// </summary>
        /// <param name="errorCode">Native error code.</param>
        /// <param name="message">Failure message.</param>
        /// <param name="errorLine">Source line of the failure.</param>
        /// <param name="func">Function of the failure.</param>
        /// <param name="file">Source file of the failure.</param>
        /// <param name="innerException">Nested failure, or null.</param>
        /// <returns>The exception to throw, never null.</returns>
        internal static Exception Create(int errorCode, string message, int errorLine, string func, string file, Exception innerException)
        {
            switch (errorCode)
            {
                case 31: // NOT_IMPLEMENTED, the one code ht4n maps to a framework type
                    return new NotImplementedException(message, innerException);

                case 1:
                    return new ProtocolException(errorCode, message, errorLine, func, file, innerException);

                case 2:
                    return new RequestTruncatedException(errorCode, message, errorLine, func, file, innerException);

                case 3:
                    return new ResponseTruncatedException(errorCode, message, errorLine, func, file, innerException);

                case 4:
                    return new TimeoutException(errorCode, message, errorLine, func, file, innerException);

                case 5:
                    return new IoException(errorCode, message, errorLine, func, file, innerException);

                case 6:
                    return new BadRootLocationException(errorCode, message, errorLine, func, file, innerException);

                case 7:
                    return new BadSchemaException(errorCode, message, errorLine, func, file, innerException);

                case 8:
                    return new InvalidMetadataException(errorCode, message, errorLine, func, file, innerException);

                case 9:
                    return new BadKeyException(errorCode, message, errorLine, func, file, innerException);

                case 10:
                    return new MetadataNotFoundException(errorCode, message, errorLine, func, file, innerException);

                case 12:
                    return new FileNotFoundException(errorCode, message, errorLine, func, file, innerException);

                case 22:
                    return new TableNotFoundException(errorCode, message, errorLine, func, file, innerException);

                case 23:
                    return new MalformedRequestException(errorCode, message, errorLine, func, file, innerException);

                case 24:
                    return new TooManyColumnsException(errorCode, message, errorLine, func, file, innerException);

                case 25:
                    return new BadDomainNameException(errorCode, message, errorLine, func, file, innerException);

                case 26:
                    return new CommandParseException(errorCode, message, errorLine, func, file, innerException);

                case 27:
                    return new ConnectMasterException(errorCode, message, errorLine, func, file, innerException);

                case 28:
                    return new ConnectHyperspaceException(errorCode, message, errorLine, func, file, innerException);

                case 29:
                    return new BadMemoryAllocationException(errorCode, message, errorLine, func, file, innerException);

                case 30:
                    return new BadScanSpecException(errorCode, message, errorLine, func, file, innerException);

                case 32:
                    return new VersionMismatchException(errorCode, message, errorLine, func, file, innerException);

                case 33:
                    return new CancelledException(errorCode, message, errorLine, func, file, innerException);

                case 34:
                    return new SchemaParseException(errorCode, message, errorLine, func, file, innerException);

                case 35:
                    return new SyntaxException(errorCode, message, errorLine, func, file, innerException);

                case 36:
                    return new DoubleUngetException(errorCode, message, errorLine, func, file, innerException);

                case 37:
                    return new EmptyBloomFilterException(errorCode, message, errorLine, func, file, innerException);

                case 38:
                    return new BloomFilterChecksumMismatchException(errorCode, message, errorLine, func, file, innerException);

                case 39:
                    return new NameAlreadyInUseException(errorCode, message, errorLine, func, file, innerException);

                case 40:
                    return new NamespaceDoesNotExistException(errorCode, message, errorLine, func, file, innerException);

                case 41:
                    return new BadNamespaceException(errorCode, message, errorLine, func, file, innerException);

                case 42:
                    return new NamespaceExistsException(errorCode, message, errorLine, func, file, innerException);

                case 43:
                    return new NoResponseException(errorCode, message, errorLine, func, file, innerException);

                case 44:
                    return new NotAllowedException(errorCode, message, errorLine, func, file, innerException);

                case 45:
                    return new InducedFailureException(errorCode, message, errorLine, func, file, innerException);

                case 46:
                    return new ServerShuttingDownException(errorCode, message, errorLine, func, file, innerException);

                case 47:
                    return new LocationUnassignedException(errorCode, message, errorLine, func, file, innerException);

                case 48:
                    return new AlreadyExistsException(errorCode, message, errorLine, func, file, innerException);

                case 49:
                    return new ChecksumMismatchException(errorCode, message, errorLine, func, file, innerException);

                case 50:
                    return new ClosedException(errorCode, message, errorLine, func, file, innerException);

                case 51:
                    return new RangeServerNotFoundException(errorCode, message, errorLine, func, file, innerException);

                case 52:
                    return new ConnectionNotInitializedException(errorCode, message, errorLine, func, file, innerException);

                case 0x00040001:
                    return new TableExistsException(errorCode, message, errorLine, func, file, innerException);

            }

            if (errorCode >= 1001 && errorCode <= 1999)
            {
                return new ConfigException(errorCode, message, errorLine, func, file, innerException);
            }

            if (errorCode >= 0x00010001 && errorCode <= 0x0001FFFF)
            {
                return new CommException(errorCode, message, errorLine, func, file, innerException);
            }

            if (errorCode >= 0x00020001 && errorCode <= 0x0002FFFF)
            {
                return new DfsBrokerException(errorCode, message, errorLine, func, file, innerException);
            }

            if (errorCode >= 0x00030001 && errorCode <= 0x0003FFFF)
            {
                return new HyperspaceException(errorCode, message, errorLine, func, file, innerException);
            }

            if (errorCode >= 0x00040001 && errorCode <= 0x0004FFFF)
            {
                return new MasterException(errorCode, message, errorLine, func, file, innerException);
            }

            if (errorCode >= 0x00050001 && errorCode <= 0x0005FFFF)
            {
                return new RangeServerException(errorCode, message, errorLine, func, file, innerException);
            }

            if (errorCode >= 0x00070001 && errorCode <= 0x0007FFFF)
            {
                return new MetaLogException(errorCode, message, errorLine, func, file, innerException);
            }

            if (errorCode >= 0x00080001 && errorCode <= 0x0008FFFF)
            {
                return new SerializationException(errorCode, message, errorLine, func, file, innerException);
            }

            if (errorCode >= 0x00090001 && errorCode <= 0x0009FFFF)
            {
                return new ThriftBrokerException(errorCode, message, errorLine, func, file, innerException);
            }

            return new HyperliteException(errorCode, message, errorLine, func, file, innerException);
        }
    }
}
