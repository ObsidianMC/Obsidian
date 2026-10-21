namespace Obsidian.Entities;

// Riding through the API: plugins mount players on mobs, boats and minecarts and take them off (see Entity.Riding.cs
// for the rest).
public partial class PlayerBase
{
    // Mobs seat players with Mount's checks; boats and minecarts with their own room checks.
    public override bool StartRiding(IEntity vehicle) => vehicle switch
    {
        Mob mob => mob.Mount(this),
        VehicleEntity boatOrMinecart => this.Vehicle is null && boatOrMinecart.Board(this),
        _ => false
    };
}
