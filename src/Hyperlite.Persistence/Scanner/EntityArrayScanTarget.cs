namespace Hyperlite.Persistence.Scanner
{
    using System;

    internal sealed class EntityArrayScanTarget : EntityScanTarget
    {
        private readonly Array array;

        private readonly int[] indexes;

        internal EntityArrayScanTarget(Type entityType, EntitySpec entitySpec, Array array, int[] indexes)
            : base(entityType, entitySpec)
        {
            this.array = array;
            this.indexes = new int[indexes.Length];
            Array.Copy(indexes, this.indexes, indexes.Length);
            this.setter = this.Set;
        }

        /// <summary>
        ///     Sets an array element for the target specified.
        /// </summary>
        private void Set(object target, object value)
        {
            lock (this.array)
            {
                this.array.SetValue(value, this.indexes);
            }
        }
    }
}
