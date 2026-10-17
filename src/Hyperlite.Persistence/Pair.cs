namespace Hyperlite.Persistence
{
    using System;

    /// <summary>
    ///     Declares a pair structure of the same type.
    /// </summary>
    /// <typeparam name="T">
    ///     The pair type.
    /// </typeparam>
    internal struct Pair<T> : IEquatable<Pair<T>>
        where T : class, IEquatable<T>
    {
        public readonly T First;

        public readonly T Second;

        /// <param name="first">
        ///     The first item.
        /// </param>
        /// <param name="second">
        ///     The second item.
        /// </param>
        public Pair(T first, T second)
        {
            this.First = first;
            this.Second = second;
        }

        /// <summary>
        ///     Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the current object is equal to the <paramref name="other" /> parameter, otherwise <c>false</c>.
        /// </returns>
        /// <param name="other">
        ///     An object to compare with this object.
        /// </param>
        public bool Equals(Pair<T> other)
        {
            return (ReferenceEquals(this.First, other.First) || (this.First != null && this.First.Equals(other.First)))
                   && (ReferenceEquals(this.Second, other.Second) ||
                       (this.Second != null && this.Second.Equals(other.Second)));
        }

        /// <summary>
        ///     Indicates whether the current object is equal to another object.
        /// </summary>
        /// <param name="other">
        ///     An object to compare with this object.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the current object is equal to the <paramref name="other" /> parameter, otherwise <c>false</c>.
        /// </returns>
        public override bool Equals(object other)
        {
            if (other is Pair<T>)
            {
                return this.Equals((Pair<T>)other);
            }

            return false;
        }

        /// <returns>
        ///     A 32-bit signed integer that is the hash code for this instance.
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return (this.First != null ? 29 * this.First.GetHashCode() : 17) +
                       29 * (this.Second != null ? this.Second.GetHashCode() : 17);
            }
        }
    }
}
