namespace Hyperlite.Persistence
{
    using System;

    public sealed class PersistenceConfiguration
    {
        private MutatorSpec mutatorSpec = new MutatorSpec(MutatorKind.Chunked)
        {
            Queued = true,
            MaxChunkSize = 32 * 1024 * 1024,
            FlushEachChunk = true,
            Capacity = 32768
        };

        private string rootNamespace = "/";

        public PersistenceConfiguration()
        {
            this.Binding = new BindingContext();

            this.UseParallelDeserialization = true;
        }

        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="configuration" /> is null.
        /// </exception>
        public PersistenceConfiguration(PersistenceConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            this.rootNamespace = configuration.RootNamespace;
            this.Binding = new BindingContext(configuration.Binding);
            this.mutatorSpec = new MutatorSpec(configuration.MutatorSpec);
            this.UseParallelDeserialization = configuration.UseParallelDeserialization;
            this.ReviewScanSpec = configuration.ReviewScanSpec;
            this.ReviewKey = configuration.ReviewKey;
            this.Context = configuration.Context;
        }

        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="configuration" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="bindingContext" /> is null.
        /// </exception>
        internal PersistenceConfiguration(PersistenceConfiguration configuration, BindingContext bindingContext)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (bindingContext == null)
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            this.rootNamespace = configuration.RootNamespace;
            this.Binding = bindingContext;
            this.mutatorSpec = new MutatorSpec(configuration.MutatorSpec);
            this.UseParallelDeserialization = configuration.UseParallelDeserialization;
            this.ReviewScanSpec = configuration.ReviewScanSpec;
            this.ReviewKey = configuration.ReviewKey;
            this.Context = configuration.Context;
        }

        /// <summary>
        ///     Gets or sets a value indicating whether to use unqualified keys or not.
        /// </summary>
        /// <value>
        ///     If <c>true</c> the persistence uses unqualified keys, otherwise <c>false</c>.
        /// </value>
        static public bool UseUnqualifiedKeys { get; set; }

        public BindingContext Binding { get; }

        /// <value>
        ///     The mutator specification.
        /// </value>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="value" /> is null.
        /// </exception>
        public MutatorSpec MutatorSpec
        {
            get { return this.mutatorSpec; }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                this.mutatorSpec = value;
            }
        }

        /// <summary>
        ///     Gets or sets the review scan spec action, if set the action is called before any scan.
        /// </summary>
        /// <value>
        ///     The review scan spec callback.
        /// </value>
        /// <remarks>
        ///     Allows scan spec updates right before a table scan starts.
        /// </remarks>
        public Action<ITable, ScanSpec> ReviewScanSpec { get; set; }

        /// <summary>
        ///     Gets or sets the review key action, if set the action is called before any object is stored.
        /// </summary>
        /// <value>
        ///     The review key callback.
        /// </value>
        /// <remarks>
        ///     Allows key updates right before writing to storage.
        /// </remarks>
        public Action<object, Key> ReviewKey { get; set; }

        public string RootNamespace
        {
            get { return this.rootNamespace; }

            set { this.rootNamespace = NormalizeNamespace(value); }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether to use parallel deserialization.
        /// </summary>
        /// <value>
        ///     If <c>true</c> the entity scanner may deserialize entities in parallel, otherwise <c>false</c>.
        /// </value>
        public bool UseParallelDeserialization { get; set; }

        /// <summary>
        ///     Gets or sets a context, any additional information to be associated with the persistence configuration and the streaming context.
        /// </summary>
        /// <value>
        ///     Any additional information to be associated with the persistence configuration and the streaming context.
        /// </value>
        public object Context { get; set; }

        /// <summary>
        ///     Normalizes the root namespace.
        /// </summary>
        /// <param name="ns">
        ///     The root namespace.
        /// </param>
        /// <returns>
        ///     The normalized root namespace.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="ns" /> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     If the trimed <paramref name="ns" /> is empty.
        /// </exception>
        public static string NormalizeNamespace(string ns)
        {
            if (ns == null)
            {
                throw new ArgumentNullException(nameof(ns));
            }

            ns = ns.Trim();
            if (string.IsNullOrEmpty(ns))
            {
                throw new ArgumentException(@"Invalid root namespace name", nameof(ns));
            }

            if (ns != "/")
            {
                if (!ns.EndsWith("/", StringComparison.Ordinal))
                {
                    ns += '/';
                }

                if (!ns.StartsWith("/", StringComparison.Ordinal))
                {
                    ns = ns.Insert(0, "/");
                }
            }

            return ns;
        }
    }
}
