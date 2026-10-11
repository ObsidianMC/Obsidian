namespace Obsidian.API.Entities;

/// <summary>
/// What a plugin's entity type is like: its size, health and speed, and what its entities do.
/// </summary>
public sealed class EntityProperties
{
    /// <summary>
    /// What the entities are: mobs, projectiles or objects. It can't change until the server restarts, since entities
    /// of the type exist as one kind.
    /// </summary>
    public EntityKind Kind { get; init; } = EntityKind.Mob;

    /// <summary>
    /// The width of the entity's hitbox, in blocks.
    /// </summary>
    public float Width { get; init; } = 0.6f;

    /// <summary>
    /// The height of the entity's hitbox, in blocks.
    /// </summary>
    public float Height { get; init; } = 1.8f;

    /// <summary>
    /// How fast a projectile or object falls, in blocks per tick per tick: 0.03 for a snowball, 0.04 for an item, 0 to
    /// float. Mobs fall like vanilla's.
    /// </summary>
    public float Gravity { get; init; } = 0.03f;

    /// <summary>
    /// The health a mob spawns with.
    /// </summary>
    public float MaxHealth { get; init; } = 20;

    /// <summary>
    /// The entity's <c>minecraft:movement_speed</c>, which its goals move it with; 0.25 is a zombie's walk.
    /// </summary>
    public float MovementSpeed { get; init; } = 0.25f;

    /// <summary>
    /// What the entity does each tick, when a player uses it and, for a projectile, when it hits something; or null for
    /// nothing.
    /// </summary>
    public IEntityBehavior? Behavior { get; init; }
}
