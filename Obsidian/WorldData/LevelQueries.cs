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
}
