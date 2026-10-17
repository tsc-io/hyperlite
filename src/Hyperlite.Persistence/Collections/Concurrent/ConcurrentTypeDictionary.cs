namespace Hyperlite.Persistence.Collections.Concurrent
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;

    /// <summary>
    ///     A dictionary keyed on a type that several threads may use at once.
    /// </summary>
    /// <remarks>
    ///     Every one of these is a cache written once per entity type and read from then on -
    ///     the binding caches, the inspectors, the type schemas - which is exactly the shape a
    ///     copy-on-write table is for: a read is a volatile load and a flat lookup with no
    ///     lock, and the rare write copies the table. See
    ///     <see cref="ConcurrentFlatDictionary{TKey, TValue, TComparer}" />.
    ///     <para>
    ///     Keys compare by reference through <see cref="TypeComparer" />, which is the
    ///     equality the framework's default gives for runtime types, without the virtual
    ///     calls.
    ///     </para>
    /// </remarks>
    internal sealed class ConcurrentTypeDictionary<TValue> : ConcurrentFlatDictionary<Type, TValue, TypeComparer>
    {
        internal ConcurrentTypeDictionary()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConcurrentTypeDictionary{TValue}" />
        ///     class with the entries given.
        /// </summary>
        internal ConcurrentTypeDictionary(IEnumerable<KeyValuePair<Type, TValue>> collection)
            : base(collection)
        {
        }

        /// <summary>
        ///     The value for the type, or the default if there is none.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TValue GetValue(Type type)
        {
            TValue value;
            return this.TryGetValue(type, out value) ? value : default(TValue);
        }

        /// <summary>
        ///     Removes the types given.
        /// </summary>
        /// <returns>
        ///     The number removed.
        /// </returns>
        internal int Remove(IEnumerable<Type> types)
        {
            if (types == null)
            {
                throw new ArgumentNullException(nameof(types));
            }

            TValue value;
            return types.ToList().Count(key => this.TryRemove(key, out value)); // ToList is required
        }
    }
}
