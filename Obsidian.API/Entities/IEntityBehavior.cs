namespace Obsidian.API.Entities;

/// <summary>
/// What the entities of a plugin's entity type do. Implement the methods the entity needs.
/// </summary>
/// <remarks>
/// The behaviour belongs to the plugin and is dropped when the plugin unloads, while its entities stay in the world,
/// so keep state on the entity or in the plugin, not here.
/// </remarks>
public interface IEntityBehavior
{
    /// <summary>
    /// Called every tick the entity is alive, after its movement.
    /// </summary>
    public ValueTask TickAsync(IEntity entity) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when a player uses the entity (right-clicks it).
    /// </summary>
    public ValueTask InteractAsync(IEntity entity, IPlayer player, InteractionHand hand) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when a projectile hits a block or a living entity, before it's removed.
    /// </summary>
    public ValueTask HitAsync(IEntity projectile, EntityHit hit) => ValueTask.CompletedTask;
}

/// <summary>
/// What a projectile hit.
/// </summary>
public sealed class EntityHit
{
    /// <summary>
    /// The living entity it hit, or null when it hit a block.
    /// </summary>
    public IEntity? Entity { get; init; }

    /// <summary>
    /// The block it hit, or null when it hit an entity.
    /// </summary>
    public Vector? Block { get; init; }

    /// <summary>
    /// Where it hit.
    /// </summary>
    public required VectorD Position { get; init; }

    /// <summary>
    /// The entity that shot the projectile, if any.
    /// </summary>
    public IEntity? Owner { get; init; }
}
