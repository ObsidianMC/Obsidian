namespace Obsidian.API.Entities;

/// <summary>
/// What the entities of a plugin's entity type do. Implement the methods the entity needs.
/// </summary>
/// <remarks>
/// The behaviour belongs to the plugin and is dropped when the plugin unloads, while its entities are kept in their
/// chunks as saved until it's loaded again, so keep state on the entity or in the plugin, not here.
/// </remarks>
public interface IEntityBehavior
{
    /// <summary>
    /// Called every tick the entity is alive, after its movement.
    /// </summary>
    public ValueTask TickAsync(IEntity entity) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when a player uses the entity (right-clicks it), before the held item's use on it.
    /// </summary>
    /// <returns>
    /// Whether the entity used the interaction, which keeps the held item from being used on it (see
    /// <see cref="Inventory.IItemBehavior.UseOnEntityAsync"/>), like a vanilla entity's interaction succeeding.
    /// </returns>
    public ValueTask<bool> InteractAsync(IEntity entity, IPlayer player, InteractionHand hand) =>
        ValueTask.FromResult(false);

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
