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
}
