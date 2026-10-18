namespace Obsidian.Entities;

public partial class Mob
{
    /// <summary>The player in the controlling seat, or null when nobody or a non-player sits there.</summary>
    public IPlayer? Rider => this.Passengers.Count > 0 ? this.Passengers[0] as IPlayer : null;

    /// <summary>Seats a player who boards the mob, if a seat is free and the player can ride.</summary>
    internal bool Mount(IPlayer player)
    {
        if (player.Vehicle is not null || !this.Alive || !player.Alive || player.GameMode == GameMode.Spectator)
            return false;

        // Every entity the server makes is an Entity; it's another engine's player that can't board.
        return player is Entity rider && rider.Ride(this);
    }

    protected virtual void TickRidden() => this.DropInvalidPassengers();

    // Passengers get off when they die, leave or change level, or the mob dies; players also by sneaking.
    private protected void DropInvalidPassengers()
    {
        foreach (var passenger in this.Passengers.ToArray())
        {
            if (passenger.Level != this.Level
                || !passenger.IsActive
                || passenger is IPlayer && passenger.Sneaking
                || !this.Alive)
                passenger.LeaveVehicle();
        }
    }
}
