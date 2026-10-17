namespace Hyperlite
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Represents a table mutator specification: which kind of mutator to create and how
    /// it batches its work.
    /// </summary>
    /// <seealso cref="MutatorKind" />
    /// <seealso cref="MutatorFlags" />
    [Serializable]
    public sealed class MutatorSpec
    {
        private static uint maxChunkSizeDefault = 64 * 1024;

        private static uint maxCellCountDefault = 4 * 1024;

        public MutatorSpec()
        {
            this.MaxChunkSize = MaxChunkSizeDefault;
            this.MaxCellCount = MaxCellCountDefault;
        }

        public MutatorSpec(MutatorKind mutatorKind)
        {
            this.MutatorKind = mutatorKind;
            this.MaxChunkSize = MaxChunkSizeDefault;
            this.MaxCellCount = MaxCellCountDefault;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MutatorSpec" /> class that is a
        /// copy of the specified instance.
        /// </summary>
        /// <param name="other">Mutator specification to copy.</param>
        public MutatorSpec(MutatorSpec other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            this.MutatorKind = other.MutatorKind;
            this.Timeout = other.Timeout;
            this.FlushInterval = other.FlushInterval;
            this.MaxChunkSize = other.MaxChunkSize;
            this.MaxCellCount = other.MaxCellCount;
            this.FlushEachChunk = other.FlushEachChunk;
            this.Queued = other.Queued;
            this.Capacity = other.Capacity;
            this.Flags = other.Flags;
        }

        /// <summary>
        /// Gets or sets the default maximum chunk size for newly created specifications.
        /// </summary>
        [CLSCompliant(false)]
        public static uint MaxChunkSizeDefault
        {
            get
            {
                return maxChunkSizeDefault;
            }

            set
            {
                maxChunkSizeDefault = value;
            }
        }

        /// <summary>
        /// Gets or sets the default maximum cell count for newly created specifications.
        /// </summary>
        [CLSCompliant(false)]
        public static uint MaxCellCountDefault
        {
            get
            {
                return maxCellCountDefault;
            }

            set
            {
                maxCellCountDefault = value;
            }
        }

        /// <summary>
        /// Gets or sets which mutator <see cref="ITable.CreateMutator(MutatorSpec)" /> returns.
        /// </summary>
        public MutatorKind MutatorKind { get; set; }

        /// <summary>
        /// Gets or sets Hypertable's limit on the time mutator methods may take; the SQLite
        /// store has none, so this changes nothing but that a negative one is refused.
        /// </summary>
        public TimeSpan Timeout { get; set; }

        /// <summary>
        /// Gets or sets Hypertable's periodic flush interval; the SQLite store does not flush
        /// periodically, so this changes nothing but that a negative one is refused. A mutator
        /// flushes when told to and when disposed, a chunked one with
        /// <see cref="FlushEachChunk" /> also after each chunk.
        /// </summary>
        public TimeSpan FlushInterval { get; set; }

        /// <summary>
        /// Gets or sets the accumulated size, in bytes, at which a chunked mutator flushes.
        /// </summary>
        [CLSCompliant(false)]
        public uint MaxChunkSize { get; set; }

        /// <summary>
        /// Gets or sets the accumulated cell count at which a chunked mutator flushes.
        /// </summary>
        [CLSCompliant(false)]
        public uint MaxCellCount { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a chunked mutator flushes after every
        /// chunk, besides when told to and when disposed.
        /// </summary>
        public bool FlushEachChunk { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the mutator hands its work to a
        /// background queue.
        /// </summary>
        public bool Queued { get; set; }

        /// <summary>
        /// Gets or sets the queue capacity, or 0 for the default.
        /// </summary>
        public int Capacity { get; set; }

        /// <summary>
        /// Kept from Hypertable's api: no flag changes what the SQLite store does.
        /// </summary>
        public MutatorFlags Flags { get; set; }

        /// <summary>
        /// Creates a specification for a regular mutator.
        /// </summary>
        /// <returns>A new mutator specification.</returns>
        public static MutatorSpec Create()
        {
            return new MutatorSpec(MutatorKind.Default);
        }

        /// <summary>
        /// Creates a specification for a regular, queued mutator.
        /// </summary>
        /// <returns>A new mutator specification.</returns>
        public static MutatorSpec CreateQueued()
        {
            return new MutatorSpec(MutatorKind.Default) { Queued = true };
        }

        /// <summary>
        /// Creates a specification for a chunked mutator.
        /// </summary>
        /// <returns>A new mutator specification.</returns>
        public static MutatorSpec CreateChunked()
        {
            return new MutatorSpec(MutatorKind.Chunked);
        }

        /// <summary>
        /// Creates a specification for a chunked, queued mutator.
        /// </summary>
        /// <returns>A new mutator specification.</returns>
        public static MutatorSpec CreateChunkedQueued()
        {
            return new MutatorSpec(MutatorKind.Chunked) { Queued = true };
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(this.GetType());
            sb.Append("(");

            if (this.Timeout.Ticks > 0)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "Timeout={0}, ", this.Timeout));
            }

            if (this.FlushInterval.Ticks > 0)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "FlushInterval={0}, ", this.FlushInterval));
            }

            if (this.MaxChunkSize > 0)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "MaxChunkSize={0}, ", this.MaxChunkSize));
            }

            if (this.MaxCellCount > 0)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "MaxCellCount={0}, ", this.MaxCellCount));
            }

            if (this.FlushEachChunk)
            {
                sb.Append("FlushEachChunk, ");
            }

            if (this.Queued)
            {
                sb.Append("Queued, ");
            }

            if (this.Capacity > 0)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "Capacity={0}, ", this.Capacity));
            }

            sb.Append(string.Format(CultureInfo.InvariantCulture, "Flags={0}", this.Flags));
            sb.Append(")");
            return sb.ToString();
        }
    }
}
