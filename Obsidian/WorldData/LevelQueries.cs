namespace Obsidian.WorldData;

internal static class LevelQueries
{
    /// <summary>
    /// The entities within <paramref name="distance"/> of <paramref name="location"/> that <paramref name="filter"/>
    /// accepts, in <see cref="ILevel.GetEntitiesInRange"/>'s order: a server level filters them before it sorts them
    /// (see <see cref="AbstractLevel.GetEntitiesInRange(VectorD, float, Func{IEntity, bool})"/>).
    /// </summary>
    public static IEnumerable<IEntity> GetEntitiesInRange(this ILevel level, VectorD location, float distance, Func<IEntity, bool> filter) =>
        level is AbstractLevel concrete
            ? concrete.GetEntitiesInRange(location, distance, filter)
            : level.GetEntitiesInRange(location, distance).Where(filter);

    /// <summary>
    /// The level's entity with <paramref name="uuid"/>, which a server level looks up by its index (see
    /// <see cref="AbstractLevel.FindEntity"/>).
    /// </summary>
    public static IEntity? FindEntity(this ILevel level, Guid uuid) =>
        level is AbstractLevel concrete
            ? concrete.FindEntity(uuid)
            : level.GetEntitiesInRange(VectorD.Zero, float.MaxValue).FirstOrDefault(entity => entity.Uuid == uuid);

    /// <summary>
    /// The level's entity with <paramref name="uuid"/> within <paramref name="distance"/> of <paramref name="location"/>,
    /// as <see cref="ILevel.GetEntitiesInRange"/> measures it (see <see cref="AbstractLevel.FindEntityInRange"/>).
    /// </summary>
    public static IEntity? FindEntityInRange(this ILevel level, Guid uuid, VectorD location, float distance) =>
        level is AbstractLevel concrete
            ? concrete.FindEntityInRange(uuid, location, distance)
            : level.GetEntitiesInRange(location, distance).FirstOrDefault(entity => entity.Uuid == uuid);
}
