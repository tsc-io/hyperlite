namespace Hyperlite.Persistence
{
    public interface IKeyBinding
    {
        /// <summary>
        ///     Creates a database key for the entity specified.
        /// </summary>
        /// <returns>
        ///     The database key.
        /// </returns>
        Key CreateKey(object entity);

        /// <summary>
        ///     Gets the database key from the entity specified.
        /// </summary>
        /// <returns>
        ///     The database key.
        /// </returns>
        Key KeyFromEntity(object entity);

        /// <summary>
        ///     Gets the database key from the value specified.
        /// </summary>
        /// <returns>
        ///     The database key.
        /// </returns>
        Key KeyFromValue(object value);

        /// <summary>
        ///     Updates the entity using the database key specified.
        /// </summary>
        /// <param name="key">
        ///     The database key.
        /// </param>
        void SetKey(object entity, Key key);
    }
}
