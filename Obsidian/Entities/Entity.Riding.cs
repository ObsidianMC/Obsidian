using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

// Riding as a relationship between any two entities: a player on a horse, a skeleton on a skeleton horse, a plugin's
// mob on another. The vehicle positions its passengers each tick (see PositionPassengers) and level travel carries the
// whole passenger tree (see Entity.Travel.cs).
public partial class Entity
{
    // Changed under the transfer lock of the level the entity is in (see EntityTransferLock): riders and their vehicle
    // share a level, so one lock covers both ends of a link. Published arrays are never mutated or exposed for writing,
    // so reads never wait for that lock, which a container click lock comes before.
    private volatile IReadOnlyList<Entity> passengers = Array.AsReadOnly<Entity>([]);
    private volatile Entity? vehicle;

    /// <summary>The entity this one rides, or null.</summary>
    internal Entity? Vehicle => this.vehicle;

    /// <summary>The entities riding this one, in seat order. The first one controls a mount.</summary>
    internal IReadOnlyList<Entity> Passengers => this.passengers;

    /// <summary>The bottom of this entity's vehicle chain: itself when it rides nothing.</summary>
    internal Entity RootVehicle
    {
        get
        {
            var root = this;
            var visited = new HashSet<Entity>();
            // Concurrent reparenting can make successive reads revisit a link even though every committed graph
            // is acyclic. Return a sampled ancestor rather than looping over a changing graph forever.
            while (visited.Add(root) && root.Vehicle is Entity vehicle)
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
        && this is not IPlayer { GameMode: GameMode.Spectator }
        && this is not IPlayer { Connected: false };

    /// <summary>How many passengers the entity seats when they board normally.</summary>
    protected virtual int MaximumPassengers => 1;

    /// <summary>Where the passenger in seat <paramref name="index"/> sits, relative to the vehicle.</summary>
    protected virtual VectorD PassengerOffset(int index) => new(0, this.Dimension.Height, 0);

    IEntity? IEntity.Vehicle => this.Vehicle;

    IReadOnlyList<IEntity> IEntity.Passengers => this.Passengers;

    public virtual bool StartRiding(IEntity vehicle) => vehicle is Entity entity && this.Ride(entity);

    public virtual void StopRiding() => this.LeaveVehicle();

    /// <summary>
    /// Boards <paramref name="vehicle"/>, leaving any current vehicle. <paramref name="force"/> skips the seat limit,
    /// as when travel puts passengers back on their vehicle.
    /// </summary>
    /// <returns>Whether the entity boarded, or its boarding was queued until the inventory transaction ends.</returns>
    internal bool Ride(Entity vehicle, bool force = false)
    {
        if (DeferRidingChange(() => this.Ride(vehicle, force)))
            return true;

        // Evaluate the virtual seat policy without the graph lock. The graph shares the level's transfer lock so its
        // level checks and travel commits agree; never acquire a tick lock or run plugin code while holding it.
        var seats = force ? 0 : vehicle.MaximumPassengers;
        Entity? previous;
        using (this.EnterOwningLevel(out var level))
        {
            // The vehicle can't leave the level while its lock is held, so they share it for as long as this holds.
            if (ReferenceEquals(vehicle, this) || ReferenceEquals(vehicle, this.Vehicle) || vehicle.Level != level)
                return false;

            // An entity waiting to be taken into the level (see CommitRespawn) is in none of its regions: a vehicle
            // carrying it would move it into one without taking it in, so it rides and carries nobody until it is.
            if (this.arrivingAt is not null || vehicle.arrivingAt is not null)
                return false;

            // Validate and publish together: opposing concurrent mounts cannot both close the same cycle.
            for (var link = vehicle; link is not null; link = link.Vehicle)
            {
                if (ReferenceEquals(link, this))
                    return false;
            }

            if (!force && vehicle.passengers.Count >= seats)
                return false;

            previous = this.Vehicle;
            if (previous is not null)
                previous.passengers = Array.AsReadOnly(previous.passengers.Where(rider => rider != this).ToArray());

            this.vehicle = vehicle;

            // Like vanilla, a player boarding a vehicle that a non-player controls takes the controlling seat.
            if (this is IPlayer && vehicle.passengers.Count > 0 && vehicle.passengers[0] is not IPlayer)
                vehicle.passengers = Array.AsReadOnly<Entity>([this, .. vehicle.passengers]);
            else
                vehicle.passengers = Array.AsReadOnly<Entity>([.. vehicle.passengers, this]);
        }

        previous?.SynchronizePassengers();
        vehicle.SynchronizePassengers();
        return true;
    }

    /// <summary>Gets off the vehicle the entity rides, if any.</summary>
    internal void LeaveVehicle()
    {
        if (DeferRidingChange(this.LeaveVehicle))
            return;

        Entity vehicle;
        using (this.EnterOwningLevel(out _))
        {
            if (this.Vehicle is not Entity current)
                return;

            vehicle = current;
            this.vehicle = null;
            vehicle.passengers = Array.AsReadOnly(vehicle.passengers.Where(rider => rider != this).ToArray());
        }

        vehicle.SynchronizePassengers();
    }

    /// <summary>Takes every passenger off this entity.</summary>
    internal void EjectPassengers()
    {
        if (DeferRidingChange(this.EjectPassengers))
            return;

        using (this.EnterOwningLevel(out _))
        {
            if (this.passengers.Count == 0)
                return;

            foreach (var passenger in this.passengers)
                passenger.vehicle = null;

            this.passengers = Array.AsReadOnly<Entity>([]);
        }

        this.SynchronizePassengers();
    }

    // Inventory hooks must not acquire a transfer lock after a click lock. Travel that already owns its level's
    // transfer lock can change the graph immediately: it must detach riders atomically with its level commit.
    private static bool DeferRidingChange(Action change) =>
        !EntityTransferLock.IsAnyHeldByCurrentThread && DeferredInventoryWork.TryDefer(change);

    /// <summary>
    /// Moves every passenger to its seat, and their passengers to theirs. A passenger whose new position its level
    /// can't hold gets off.
    /// </summary>
    internal void PositionPassengers()
    {
        var riders = this.Passengers;
        for (var index = 0; index < riders.Count; index++)
        {
            var passenger = riders[index];
            if (passenger.Vehicle != this)
                continue;

            var position = this.Position + this.PassengerOffset(index);
            var generation = this switch
            {
                Boat boat when passenger is IPlayer rider => boat.ClientControl.AcceptedGeneration(rider),
                PluginMob mob when passenger is IPlayer rider => mob.ClientControl.AcceptedGeneration(rider),
                _ => null
            };
            if (passenger is PlayerBase player && generation is long clientMove)
            {
                if (!player.PositionFromClientVehicle(this, position, clientMove))
                    passenger.LeaveVehicle();

                passenger.PositionPassengers();
                continue;
            }

            if (this.Level is AbstractLevel level && !level.TryMoveEntity(passenger, passenger.Position, position))
            {
                passenger.LeaveVehicle();
                continue;
            }

            passenger.LastPosition = passenger.Position;
            passenger.Position = position;
            if (passenger is not IPlayer)
                passenger.BoundingBox = passenger.Dimension.CreateBBFromPosition(position);

            passenger.PositionPassengers();
        }
    }

    /// <summary>Tells a player that just started tracking this entity who rides it.</summary>
    internal ValueTask SendPassengersToAsync(Player player) => this.Passengers.Count == 0
        ? default
        : player.Client.QueuePacketAsync(this.CreatePassengersPacket());

    private protected void SynchronizePassengers() =>
        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, this.CreatePassengersPacket());

    private SetPassengersPacket CreatePassengersPacket() => new()
    {
        EntityId = this.EntityId,
        Passengers = this.Passengers.Select(passenger => passenger.EntityId).ToArray()
    };
}
