namespace Hyperlite.Persistence.Scanner
{
    /// <returns>
    ///     <c>true</c> if the entity has been found; otherwise <c>false</c>.
    /// </returns>
    internal delegate bool TryGetFetchedEntity(EntitySpec entitySpec, out object entity);
}
