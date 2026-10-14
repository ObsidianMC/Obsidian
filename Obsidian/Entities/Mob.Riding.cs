namespace Obsidian.Entities;

public partial class Mob
{
    /// <summary>The player in the controlling seat, or null when nobody or a non-player sits there.</summary>
    protected Player? Rider => this.Passengers.Count > 0 ? this.Passengers[0] as Player : null;

    /// <summary>Seats a player who boards the mob, if a seat is free and the player can ride.</summary>
    internal bool Mount(Player player)
    {
        if (player.Vehicle is not null || !this.Alive || !player.Alive || player.GameMode == GameMode.Spectator)
            return false;

        return player.Ride(this);
    }

    protected virtual void TickRidden() => this.DropInvalidPassengers();

    // Passengers get off when they die, leave or change level, or the mob dies; players also by sneaking.
    private protected void DropInvalidPassengers()
    {
        foreach (var passenger in this.Passengers.ToArray())
        {
            if (passenger.Level != this.Level
                || !passenger.IsActive
                || passenger is Player && passenger.Sneaking
                || !this.Alive)
                passenger.LeaveVehicle();
        }
    }
}
