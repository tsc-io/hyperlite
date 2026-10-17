namespace Hyperlite.Persistence
{
    using System;

    ////TODO review behaviors naming

    /// <summary>
    ///     The entity manager behaviors.
    /// </summary>
    [Flags]
    public enum Behaviors
    {
        /// <summary>
        ///     Use the entity manager default behaviors.
        /// </summary>
        Default = 0,

        /// <summary>
        ///     Creates always a new entity key for each entity in the object graph.
        /// </summary>
        CreateAlways = 1,

        /// <summary>
        ///     Creates new entity keys for new entities and those which are not part of the current EntityManager context.
        /// </summary>
        CreateLazy = 2,

        /// <summary>
        ///     Creates new entity keys only for new entities.
        /// </summary>
        CreateNew = 3,

        /// <summary>
        ///     Masks for all create behaviors.
        /// </summary>
        CreateBehaviors = CreateAlways | CreateLazy | CreateNew,

        /// <summary>
        ///     Writes only the new entities in a object graph.
        /// </summary>
        WriteNewOnly = 1 << 8,

        /// <summary>
        ///     Writes an entity only once.
        /// </summary>
        WriteOnce = 1 << 9,

        /// <summary>
        ///     Don't cache entities written nor entities read, cannot be combined with Behaviors.CreateLazy.
        /// </summary>
        DoNotCache = 1 << 10,

        /// <summary>
        ///     Bypasses the entity keys written cache, cannot be combined with Behaviors.CreateLazy.
        /// </summary>
        BypassWriteCache = 1 << 11,

        /// <summary>
        ///     Writes as though this entity manager had fetched nothing: an entity is written
        ///     whether or not it was fetched, and under Behaviors.CreateLazy one fetched is given
        ///     a new key. What a fetch records is unaffected.
        /// </summary>
        BypassReadCache = 1 << 12
    }
}
