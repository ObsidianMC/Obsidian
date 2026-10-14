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
    /// A mob's category, like a vanilla entity type's: NeoForge's <c>add_spawns</c> biome modifiers add the mob to
    /// this category's spawn lists, and naturally spawned mobs count against its cap. Misc mobs don't spawn naturally.
    /// </summary>
    public MobCategory Category { get; init; } = MobCategory.Misc;

    /// <summary>
    /// Where the entity's passengers sit, like a vanilla entity type's passenger attachments: a point per seat, in
    /// blocks from the entity's position at its feet, for the entity facing south (+Z ahead, +X to its left), turned
    /// with its yaw. The first passenger sits at the first point, the second at the second, and passengers past the
    /// last point at the last. A rider's feet go to the point, or 0.6 blocks below it for a player, as vanilla places
    /// players. None, the default, seats every passenger on top of the hitbox, vanilla's default.
    /// </summary>
    /// <remarks>
    /// The server places riders at the points, and the Obsidian client places them there as it draws them and the
    /// player's camera, so give the type the same points on both sides.
    /// </remarks>
    public IReadOnlyList<VectorF> PassengerAttachments { get; init; } = [];

    /// <summary>
    /// What the entity does each tick, when a player uses it and, for a projectile, when it hits something; or null for
    /// nothing.
    /// </summary>
    public IEntityBehavior? Behavior { get; init; }
}
