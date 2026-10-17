namespace Hyperlite.Persistence.Bindings
{
    using System;
    using System.Linq;
    using System.Linq.Expressions;
    using Hyperlite.Persistence.Reflection;

    /// <typeparam name="T">
    ///     The entity type.
    /// </typeparam>
    public sealed class KeyBinding<T> : PartialKeyBinding
        where T : class
    {
        /// <summary>
        ///     Indicating whether this key binding should generate new keys or not.
        /// </summary>
        private readonly bool generateKey;

        private readonly Func<T, string> get;

        private readonly Action<T, string> set;

        public KeyBinding(Expression<Func<T, string>> propertyLambda)
            : this(propertyLambda, false)
        {
        }

        /// <param name="generateKey">
        ///     Indicating whether this key binding should generate new keys or not.
        /// </param>
        public KeyBinding(Expression<Func<T, string>> propertyLambda, bool generateKey)
            : base(typeof(T))
        {
            FromExpression(propertyLambda, out this.get, out this.set);
            this.generateKey = generateKey;
        }

        public KeyBinding(Expression<Func<T, string>> propertyLambda, IColumnBinding columnBinding)
            : this(propertyLambda, columnBinding, false)
        {
        }

        /// <param name="generateKey">
        ///     Indicating whether this key binding should generate new keys or not.
        /// </param>
        public KeyBinding(Expression<Func<T, string>> propertyLambda, IColumnBinding columnBinding, bool generateKey)
            : base(typeof(T), columnBinding)
        {
            FromExpression(propertyLambda, out this.get, out this.set);
            this.generateKey = generateKey;
        }

        /// <param name="get">
        ///     The getter function.
        /// </param>
        /// <param name="set">
        ///     The setter method.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="get" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="set" /> is null.
        /// </exception>
        public KeyBinding(Func<T, string> get, Action<T, string> set)
            : this(get, set, false)
        {
        }

        /// <param name="get">
        ///     The getter function.
        /// </param>
        /// <param name="set">
        ///     The setter method.
        /// </param>
        /// <param name="generateKey">
        ///     Indicating whether this key binding should generate new keys or not.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="get" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="set" /> is null.
        /// </exception>
        public KeyBinding(Func<T, string> get, Action<T, string> set, bool generateKey)
            : base(typeof(T))
        {
            if (get == null)
            {
                throw new ArgumentNullException(nameof(get));
            }

            if (set == null)
            {
                throw new ArgumentNullException(nameof(set));
            }

            this.get = get;
            this.set = set;

            this.generateKey = generateKey;
        }

        /// <param name="get">
        ///     The getter function.
        /// </param>
        /// <param name="set">
        ///     The setter method.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="get" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="set" /> is null.
        /// </exception>
        public KeyBinding(Func<T, string> get, Action<T, string> set, IColumnBinding columnBinding)
            : this(get, set, columnBinding, false)
        {
        }

        /// <param name="get">
        ///     The getter function.
        /// </param>
        /// <param name="set">
        ///     The setter method.
        /// </param>
        /// <param name="generateKey">
        ///     Indicating whether this key binding should generate new keys or not.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="get" /> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="set" /> is null.
        /// </exception>
        public KeyBinding(Func<T, string> get, Action<T, string> set, IColumnBinding columnBinding, bool generateKey)
            : base(typeof(T), columnBinding)
        {
            if (get == null)
            {
                throw new ArgumentNullException(nameof(get));
            }

            if (set == null)
            {
                throw new ArgumentNullException(nameof(set));
            }

            this.get = get;
            this.set = set;

            this.generateKey = generateKey;
        }

        /// <inheritdoc />
        public override Key CreateKey(object entity)
        {
            if (this.generateKey)
            {
                var key = this.GenerateKey(new Key(), entity.GetType());
                this.set((T)entity, key.Row);
                return key;
            }

            return this.KeyFromEntity(entity);
        }

        /// <inheritdoc />
        public override Key KeyFromEntity(object entity)
        {
            return this.Merge(new Key(this.get((T)entity)));
        }

        /// <inheritdoc />
        public override Key KeyFromValue(object value)
        {
            var key = value as string;
            return key != null ? this.Merge(new Key(key)) : base.KeyFromValue(value);
        }

        /// <inheritdoc />
        public override void SetKey(object entity, Key key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            this.set((T)entity, key.Row);
            this.Timestamp(entity, key);
        }

        /// <summary>
        ///     Gets the setter and getter from the expression specified.
        /// </summary>
        /// <param name="getter">
        ///     Receives the getter function.
        /// </param>
        /// <param name="setter">
        ///     Receives the setter method.
        /// </param>
        private static void FromExpression(Expression<Func<T, string>> propertyLambda, out Func<T, string> getter,
            out Action<T, string> setter)
        {
            var member = (MemberExpression)propertyLambda.Body;
            var inspector = Inspector.InspectorForType(member.Member.DeclaringType);
            var property = inspector.Properties.FirstOrDefault(p => p.Member == member.Member);
            if (property != null)
            {
                property.Ignore = true;
            }

            DelegateFactory.CreateAccessors(propertyLambda, out getter, out setter);
        }
    }
}
