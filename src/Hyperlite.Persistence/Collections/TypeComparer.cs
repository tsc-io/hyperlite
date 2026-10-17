namespace Hyperlite.Persistence.Collections
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>
    ///     Equality of <see cref="Type" /> keys by reference.
    /// </summary>
    /// <remarks>
    ///     The runtime keeps one <see cref="Type" /> object per type, and
    ///     <c>RuntimeType.Equals</c> is a reference compare, so for every type the runtime
    ///     hands out - <c>GetType()</c>, <c>typeof</c>, reflection - this is the same
    ///     equality the framework's default comparer gives, without the virtual
    ///     <c>Equals</c> and <c>GetHashCode</c> calls. It differs only for a user-defined
    ///     <see cref="Type" /> subclass such as <c>TypeDelegator</c>, which no key here is.
    /// </remarks>
    internal struct TypeComparer : IEqualityComparer<Type>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Type x, Type y)
        {
            return ReferenceEquals(x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(Type obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
