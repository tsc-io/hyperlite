namespace Hyperlite.Persistence
{
    public static class BehaviorExtensions
    {
        /// <summary>
        ///     Gets a value indicating whether the behavior is bypass read cache.
        /// </summary>
        /// <return>
        ///     <c>true</c> if bypass read cache behaviors, otherwise <c>false</c>.
        /// </return>
        public static bool BypassReadCache(this Behaviors behaviors)
        {
            return (behaviors & Behaviors.BypassReadCache) > 0;
        }

        /// <summary>
        ///     Gets a value indicating whether the behavior is bypass write cache.
        /// </summary>
        /// <return>
        ///     <c>true</c> if bypass write cache behaviors, otherwise <c>false</c>.
        /// </return>
        public static bool BypassWriteCache(this Behaviors behaviors)
        {
            return (behaviors & Behaviors.BypassWriteCache) > 0;
        }

        /// <summary>
        ///     Gets a value indicating whether the behavior is don't cache.
        /// </summary>
        /// <return>
        ///     <c>true</c> if don't cache behaviors, otherwise <c>false</c>.
        /// </return>
        public static bool DoNotCache(this Behaviors behaviors)
        {
            return (behaviors & Behaviors.DoNotCache) > 0;
        }

        /// <summary>
        ///     Gets a value indicating whether the behavior is create always.
        /// </summary>
        /// <return>
        ///     <c>true</c> if create always behaviors, otherwise <c>false</c>.
        /// </return>
        public static bool IsCreateAlways(this Behaviors behaviors)
        {
            return (behaviors & Behaviors.CreateBehaviors) == Behaviors.CreateAlways;
        }

        /// <summary>
        ///     Gets a value indicating whether the behavior is create lazy.
        /// </summary>
        /// <return>
        ///     <c>true</c> if create lazy behaviors, otherwise <c>false</c>.
        /// </return>
        public static bool IsCreateLazy(this Behaviors behaviors)
        {
            return (behaviors & Behaviors.CreateBehaviors) == Behaviors.CreateLazy;
        }

        /// <summary>
        ///     Gets a value indicating whether the behavior is create new.
        /// </summary>
        /// <return>
        ///     <c>true</c> if create new behaviors, otherwise <c>false</c>.
        /// </return>
        public static bool IsCreateNew(this Behaviors behaviors)
        {
            return (behaviors & Behaviors.CreateBehaviors) == Behaviors.CreateNew;
        }

        /// <summary>
        ///     Gets a value indicating whether the behavior is default.
        /// </summary>
        /// <return>
        ///     <c>true</c> if default behaviors, otherwise <c>false</c>.
        /// </return>
        public static bool IsDefault(this Behaviors behaviors)
        {
            return (behaviors & Behaviors.CreateBehaviors) == Behaviors.Default;
        }

        /// <summary>
        ///     Gets a value indicating whether the behavior is write new only.
        /// </summary>
        /// <return>
        ///     <c>true</c> if write new only behaviors, otherwise <c>false</c>.
        /// </return>
        public static bool WriteNewOnly(this Behaviors behaviors)
        {
            return (behaviors & Behaviors.WriteNewOnly) > 0;
        }

        /// <summary>
        ///     Gets a value indicating whether the behavior is write once.
        /// </summary>
        /// <return>
        ///     <c>true</c> if write once behaviors, otherwise <c>false</c>.
        /// </return>
        public static bool WriteOnce(this Behaviors behaviors)
        {
            return (behaviors & Behaviors.WriteOnce) > 0;
        }
    }
}
