namespace Hyperlite.Persistence.Scanner
{
    using System;
    using System.Collections.Generic;
    using Hyperlite.Persistence.Collections;
    using Hyperlite.Persistence.Reflection;

    internal class EntityScanTarget : EntitySpec
    {
        /// <summary>
        ///     The scan target setter action.
        /// </summary>
        protected Action<object, object> setter;

        private readonly object syncRoot = new object();

        /// <summary>
        ///     The scan target.
        /// </summary>
        private readonly object target;

        private ICollection<EntityScanTarget> scanTargetRefs;

        private object value;

        /// <param name="key">
        ///     The entity key.
        /// </param>
        internal EntityScanTarget(EntityReference entityReference, object key)
            : this(entityReference, entityReference.GetKeyFromObject(key, true))
        {
        }

        /// <param name="key">
        ///     The entity key.
        /// </param>
        internal EntityScanTarget(EntityReference entityReference, Key key)
            : base(entityReference, key)
        {
        }

        /// <param name="key">
        ///     The entity key.
        /// </param>
        /// <param name="entitySink">
        ///     The entity sink, receives the entities fetched.
        /// </param>
        internal EntityScanTarget(EntityReference entityReference, Key key, Action<object> entitySink)
            : base(entityReference, key)
        {
            // no need for an additional field
            this.setter = (t, v) => entitySink(v);
        }

        /// <param name="entitySpec">
        ///     The entity specification.
        /// </param>
        /// <param name="target">
        ///     The scan target.
        /// </param>
        internal EntityScanTarget(InspectedProperty property, EntitySpec entitySpec, object target)
            : base(property.PropertyType, entitySpec)
        {
            this.target = target;
            this.setter = property.Setter;
        }

        /// <param name="setter">
        ///     The scan target setter action.
        /// </param>
        internal EntityScanTarget(Type entityType, EntitySpec entitySpec, Action<object, object> setter)
            : base(entityType, entitySpec)
        {
            this.setter = setter;
        }

        /// <param name="entitySpec">
        ///     The entity specification.
        /// </param>
        protected EntityScanTarget(Type entityType, EntitySpec entitySpec)
            : base(entityType, entitySpec)
        {
        }

        /// <param name="entitySpec">
        ///     The entity specification.
        /// </param>
        /// <param name="key">
        ///     The entity key.
        /// </param>
        protected EntityScanTarget(Type entityType, EntitySpec entitySpec, Key key)
            : base(entityType, entitySpec, key)
        {
        }

        internal object Value => this.value;

        internal void AddScanTargetRef(EntityScanTarget entityScanTarget)
        {
            lock (this.syncRoot)
            {
                if (this.scanTargetRefs == null)
                {
                    this.scanTargetRefs = new ChunkedCollection<EntityScanTarget>();
                }

                this.scanTargetRefs.Add(entityScanTarget);
            }
        }

        /// <summary>
        ///     Sets the value specified to the scan target and all attached scan target references.
        /// </summary>
        internal void SetValue(object v)
        {
            this.setter?.Invoke(this.target, v);

            this.value = v;

            if (this.scanTargetRefs != null)
            {
                foreach (var entityScanTarget in this.scanTargetRefs)
                {
                    entityScanTarget.SetValue(v);
                }
            }
        }
    }
}
