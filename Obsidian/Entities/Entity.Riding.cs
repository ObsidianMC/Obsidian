using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

// Riding as a relationship between any two entities: a player on a horse, a skeleton on a skeleton horse, a plugin's
// mob on another. The vehicle positions its passengers each tick (see PositionPassengers) and level travel carries the
// whole passenger tree (see Entity.Travel.cs).
public partial class Entity
{
    private readonly List<Entity> passengers = [];

    /// <summary>The entity this one rides, or null.</summary>
    internal Entity? Vehicle { get; private set; }

    /// <summary>The entities riding this one, in seat order. The first one controls a mount.</summary>
    internal IReadOnlyList<Entity> Passengers => this.passengers;

    /// <summary>The bottom of this entity's vehicle chain: itself when it rides nothing.</summary>
    internal Entity RootVehicle
    {
        get
        {
            var root = this;
            while (root.Vehicle is Entity vehicle)
                root = vehicle;

            return root;
        }
    }

    /// <summary>
    /// Whether the entity is alive, not removed and not a spectator, like vanilla's check before an entity uses
    /// portals or holds a lead.
    /// </summary>
    internal bool IsActive => this is not Living { Alive: false }
        && this is not Mob { IsRemoved: true }
        && this is not ItemEntity { Removed: true }
        && this is not Player { GameMode: GameMode.Spectator }
        && this is not Player { Client.Connected: false };

    /// <summary>How many passengers the entity seats when they board normally.</summary>
    protected virtual int MaximumPassengers => 1;

    /// <summary>Where the passenger in seat <paramref name="index"/> sits, relative to the vehicle.</summary>
    protected virtual VectorD PassengerOffset(int index) => new(0, this.Dimension.Height, 0);

    IEntity? IEntity.Vehicle => this.Vehicle;

    IReadOnlyList<IEntity> IEntity.Passengers => this.passengers.ToArray();

    public virtual bool StartRiding(IEntity vehicle) => vehicle is Entity entity && this.Ride(entity);

    public virtual void StopRiding() => this.LeaveVehicle();

    /// <summary>
    /// Boards <paramref name="vehicle"/>, leaving any current vehicle. <paramref name="force"/> skips the seat limit,
    /// as when travel puts passengers back on their vehicle.
    /// </summary>
    /// <returns>Whether the entity now rides the vehicle.</returns>
    internal bool Ride(Entity vehicle, bool force = false)
    {
        if (ReferenceEquals(vehicle, this) || ReferenceEquals(vehicle, this.Vehicle) || vehicle.Level != this.Level)
            return false;

        // A vehicle can't end up riding its own passenger.
        for (var link = vehicle; link is not null; link = link.Vehicle)
        {
            if (ReferenceEquals(link, this))
                return false;
        }

        if (!force && vehicle.passengers.Count >= vehicle.MaximumPassengers)
            return false;

        this.LeaveVehicle();
        this.Vehicle = vehicle;

        // Like vanilla, a player boarding a vehicle that a non-player controls takes the controlling seat.
        if (this is Player && vehicle.passengers.Count > 0 && vehicle.passengers[0] is not Player)
            vehicle.passengers.Insert(0, this);
        else
            vehicle.passengers.Add(this);

        vehicle.SynchronizePassengers();
        return true;
    }

    /// <summary>Gets off the vehicle the entity rides, if any.</summary>
    internal void LeaveVehicle()
    {
        if (this.Vehicle is not Entity vehicle)
            return;

        this.Vehicle = null;
        if (vehicle.passengers.Remove(this))
            vehicle.SynchronizePassengers();
    }

    /// <summary>Takes every passenger off this entity.</summary>
    internal void EjectPassengers()
    {
        if (this.passengers.Count == 0)
            return;

        foreach (var passenger in this.passengers)
            passenger.Vehicle = null;

        this.passengers.Clear();
        this.SynchronizePassengers();
    }

    /// <summary>
    /// Moves every passenger to its seat, and their passengers to theirs. A passenger whose new position its level
    /// can't hold gets off.
    /// </summary>
    internal void PositionPassengers()
    {
        for (var index = 0; index < this.passengers.Count; index++)
        {
            var passenger = this.passengers[index];
            var position = this.Position + this.PassengerOffset(index);
            if (this.Level is AbstractLevel level && !level.TryMoveEntity(passenger, passenger.Position, position))
            {
                passenger.LeaveVehicle();
                index--;
                continue;
            }

            passenger.LastPosition = passenger.Position;
            passenger.Position = position;
            if (passenger is not Player)
                passenger.BoundingBox = passenger.Dimension.CreateBBFromPosition(position);

            passenger.PositionPassengers();
        }
    }

    /// <summary>Tells a player that just started tracking this entity who rides it.</summary>
    internal ValueTask SendPassengersToAsync(Player player) => this.passengers.Count == 0
        ? default
        : player.Client.QueuePacketAsync(this.CreatePassengersPacket());

    private protected void SynchronizePassengers() =>
        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, this.CreatePassengersPacket());

    private SetPassengersPacket CreatePassengersPacket() => new()
    {
        EntityId = this.EntityId,
        Passengers = this.passengers.Select(passenger => passenger.EntityId).ToArray()
    };
}
