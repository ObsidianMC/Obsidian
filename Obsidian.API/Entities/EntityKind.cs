namespace Obsidian.API.Entities;

/// <summary>
/// What a plugin's entity type's entities are. A client mod's entity should be the same kind, so that the entity data
/// the server sends matches what it reads.
/// </summary>
public enum EntityKind
{
    /// <summary>
    /// A mob: it has health, falls, takes damage and runs goals.
    /// </summary>
    Mob,

    /// <summary>
    /// A projectile, like a snowball: it flies along its motion, slowed by drag and pulled down by its gravity, and
    /// hits the first block or living entity in its way (see <see cref="IEntityBehavior.HitAsync"/>), which removes it.
    /// </summary>
    Projectile,

    /// <summary>
    /// An object without health: it falls by its gravity and comes to rest where it lands, until it's removed.
    /// </summary>
    Object
}
