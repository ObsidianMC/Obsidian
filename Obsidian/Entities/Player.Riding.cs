namespace Obsidian.Entities;

// Riding through the API: plugins mount players on mobs and take them off.
public partial class Player
{
    IEntity? IPlayer.Vehicle => this.Vehicle;

    public bool StartRiding(IEntity vehicle) => vehicle is Mob mob && mob.Mount(this);

    public void StopRiding() => this.Vehicle?.Dismount(this);
}
