namespace Hyperlite.Persistence.Scanner
{
    using System;
    using System.Collections.Generic;
    using Hyperlite.Persistence.Collections;

    internal sealed class EntityScanResult : EntitySpec
    {
        /// <summary>
        ///     The result collection.
        /// </summary>
        private readonly ChunkedCollection<object> collection = new ChunkedCollection<object>();

        /// <summary>
        ///     The entity sink, receives the entities fetched.
        /// </summary>
        private readonly Action<object> valueSink;

        internal EntityScanResult(EntityReference entityReference)
            : base(entityReference, null)
        {
            this.collection = new ChunkedCollection<object>();
            this.valueSink = v =>
            {
                lock (this.collection.SyncRoot)
                {
                    this.collection.Add(v);
                }
            };
        }

        /// <param name="entitySink">
        ///     The entity sink, receives the entities fetched.
        /// </param>
        internal EntityScanResult(EntityReference entityReference, Action<object> entitySink)
            : base(entityReference, null)
        {
            this.valueSink = entitySink;
        }

        internal ICollection<object> Values => this.collection;

        /// <value>
        ///     The values.
        /// </value>
        internal Action<object> ValueSink => this.valueSink;
    }
}
