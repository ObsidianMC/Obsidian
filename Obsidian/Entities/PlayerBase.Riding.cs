namespace Obsidian.Entities;

// Riding through the API: plugins mount players on mobs and take them off (see Entity.Riding.cs for the rest).
public partial class PlayerBase
{
    // Players keep the mob-only contract of IPlayer.StartRiding, with Mount's seat and spectator checks.
    public override bool StartRiding(IEntity vehicle) => vehicle is Mob mob && mob.Mount(this);
}
