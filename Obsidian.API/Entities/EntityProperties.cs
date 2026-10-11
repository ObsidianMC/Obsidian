namespace Obsidian.API.Entities;

/// <summary>
/// What a plugin's entity type is like: its size, health and speed, and what its entities do.
/// </summary>
public sealed class EntityProperties
{
    /// <summary>
    /// The width of the entity's hitbox, in blocks.
    /// </summary>
    public float Width { get; init; } = 0.6f;

    /// <summary>
    /// The height of the entity's hitbox, in blocks.
    /// </summary>
    public float Height { get; init; } = 1.8f;

    /// <summary>
    /// The health the entity spawns with.
    /// </summary>
    public float MaxHealth { get; init; } = 20;

    /// <summary>
    /// The entity's <c>minecraft:movement_speed</c>, which its goals move it with; 0.25 is a zombie's walk.
    /// </summary>
    public float MovementSpeed { get; init; } = 0.25f;

    /// <summary>
    /// What the entity does each tick and when a player uses it, or null for nothing.
    /// </summary>
    public IEntityBehavior? Behavior { get; init; }
}
