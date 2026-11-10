using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class Entity
{
    // Whether this projectile has emitted projectile_shoot (vanilla's Projectile.hasBeenShot).
    private bool hasBeenShot;

    /// <summary>
    /// Vanilla's <c>Projectile.tick</c>: the first time a projectile ticks, it emits <c>projectile_shoot</c> where it
    /// is then, with <paramref name="owner"/> as the source. Each projectile calls it where its tick calls the base
    /// tick: arrows after they've moved and hit, other projectiles before they hit.
    /// </summary>
    internal void EmitShotOnce(IEntity? owner)
    {
        if (this.hasBeenShot)
            return;

        this.hasBeenShot = true;
        if (this.Level is AbstractLevel level)
            level.EmitGameEvent(MobGameEvent.ProjectileShoot, this.Position, owner);
    }
}
