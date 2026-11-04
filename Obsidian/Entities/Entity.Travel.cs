using Obsidian.API.World;
using Obsidian.WorldData;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Portals;

namespace Obsidian.Entities;

public partial class Entity
{
    /// <summary>Transfers the existing instance, including its state, instead of spawning a replacement.</summary>
    public virtual async ValueTask TeleportAsync(
        ILevel level, VectorD position, Angle? yaw = null, Angle? pitch = null)
    {
        ValidateTravel(level, position, yaw, pitch);
        this.CancelEntityPortalTravel();
        var source = this.Level;
        using var area = level is AbstractLevel destination
            ? await PortalArea.LoadAsync(destination, (Vector)position.Floor(), 0, () => this.IsTravelResident(source))
            : null;
        if (level is AbstractLevel && area is null)
            throw new InvalidOperationException("The entity moved while the destination was loading.");

        Entity[] riders;
        VectorD from;
        Angle fromYaw;
        Angle fromPitch;
        using (TransferLockOf(source).Enter())
        {
            this.ValidateTravelResident(source);

            // Like vanilla, an entity teleported on its own gets off its vehicle, and its passengers get off to travel
            // separately and board again at the destination.
            riders = this.DetachPassengersForTravel();
            from = this.Position;
            fromYaw = this.Yaw;
            fromPitch = this.Pitch;

            // Within the level the move is committed with the detach; travel to another level can't take the
            // destination's lock while this one is held, so it's committed below.
            if (source == level)
                this.CommitTravelOrReseat(riders, source, level, position, yaw, pitch);
        }

        if (source != level)
            this.CommitTravelOrReseat(riders, source, level, position, yaw, pitch);

        await this.CarryPassengersAsync(riders, from, fromYaw, fromPitch);
    }

    // Commits a teleport, seating the passengers taken off for it again if it fails.
    private void CommitTravelOrReseat(
        Entity[] riders, ILevel source, ILevel level, VectorD position, Angle? yaw, Angle? pitch)
    {
        Action arrived = source == level
            ? () => this.QueueTeleport(level, position)
            : this.AppearInNewLevel;

        try
        {
            this.CommitTravel(source, level, position, yaw, pitch, arrived);
        }
        catch (InvalidOperationException)
        {
            foreach (var rider in riders)
                rider.Ride(this, force: true);

            throw;
        }
    }

    private void QueueTeleport(ILevel level, VectorD position) =>
        this.PacketBroadcaster.QueuePacketToLevel(level, new TeleportEntityPacket
        {
            EntityId = this.EntityId,
            Position = position,
            Delta = this.Motion,
            Yaw = this.Yaw,
            Pitch = this.Pitch,
            OnGround = this.MovementFlags.HasFlag(MovementFlags.OnGround)
        });

    // Spawned for the new level's players as the level takes it in, so a removal can't come between and leave it on
    // their clients.
    private void AppearInNewLevel()
    {
        if (this is Mob mob)
        {
            mob.HoldLeashByUuid();
            mob.ResetMovementBaseline();
        }

        this.SpawnEntity();
    }

    /// <summary>
    /// Moves passengers taken off this entity before it travelled to the same place relative to it, each with its own
    /// passengers, and seats them again. A passenger that can't follow, such as one removed meanwhile, stays behind.
    /// </summary>
    private protected async ValueTask CarryPassengersAsync(
        Entity[] riders, VectorD from, Angle fromYaw, Angle fromPitch)
    {
        foreach (var rider in riders)
        {
            // Vanilla's passenger transition: the same offset from the vehicle, turned as much as the vehicle turned.
            var riderYaw = this.Yaw.Degrees + (rider.Yaw.Degrees - fromYaw.Degrees);
            var riderPitch = this.Pitch.Degrees + (rider.Pitch.Degrees - fromPitch.Degrees);
            try
            {
                await rider.TeleportAsync(this.Level, this.Position + (rider.Position - from), riderYaw, riderPitch);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            rider.Ride(this, force: true);
        }
    }

    /// <summary>Captures and detaches the passengers that follow this entity, including a player vehicle.</summary>
    private protected Entity[] DetachPassengersForTravel()
    {
        using (this.EnterOwningLevel(out _))
        {
            this.LeaveVehicle();
            var riders = this.Passengers.ToArray();
            this.EjectPassengers();
            return riders;
        }
    }

    // Membership is authoritative for ordinary removal and unloading saves; item pickup and mob death may mark
    // removal before their dictionary entry is taken out. Read and revalidate under the source's transfer lock.
    private protected bool IsTravelResident(ILevel source)
    {
        using (TransferLockOf(source).Enter())
        {
            return this.Level == source
                && this is not ItemEntity { Removed: true }
                && this is not Mob { IsRemoved: true }
                && source is AbstractLevel origin
                && origin.GetRegionForLocation(this.Position) is IRegion region
                && region.Entities.TryGetValue(this.EntityId, out var entity)
                && ReferenceEquals(entity, this);
        }
    }

    private void ValidateTravelResident(ILevel source)
    {
        if (!this.IsTravelResident(source))
            throw new InvalidOperationException("The entity was removed or moved while the destination was loading.");
    }

    /// <summary>
    /// Moves the entity to <paramref name="position"/> in <paramref name="destination"/>. Between levels it's a handoff
    /// that never holds both levels' transfer locks: the source lets the entity go under its own lock (see
    /// <see cref="Depart"/>), then the destination takes it in under its own (see <see cref="Arrive"/>), or sends it
    /// back the same way when it can't. In between it's in neither level and owned by the destination, and a removal
    /// meanwhile (see <see cref="TryRemoveOnArrival"/>) keeps it from arriving.
    /// </summary>
    /// <param name="arrived">
    /// Run under the destination's transfer lock once the entity is there, before anything else can reach it there.
    /// </param>
    /// <remarks>
    /// Call holding no level's transfer lock, except the level's own for a move within it. No awaits or tick-lock
    /// acquisition here: saves take tick lock, then transfer lock, then chunk entity lock.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The entity isn't in <paramref name="source"/> anymore, the destination couldn't take it, or it was removed on
    /// the way.
    /// </exception>
    private protected void CommitTravel(
        ILevel source, ILevel destination, VectorD position, Angle? yaw, Angle? pitch, Action? arrived = null)
    {
        if (source == destination)
        {
            this.RelocateWithinLevel(source, position, yaw, pitch, arrived);
            return;
        }

        if (source is not AbstractLevel)
            throw new NotSupportedException("Entity transfers require an Obsidian source level.");

        var departure = this.Depart(source, destination, position, yaw, pitch);
        var arrival = this.Arrive(destination, arrived, departure);
        if (arrival == Arrival.Refused)
        {
            this.Arrive(source, arrived: null, returning: null);
            throw new InvalidOperationException("The destination level could not accept the entity.");
        }

        if (arrival == Arrival.Removed)
            throw new InvalidOperationException("The entity was removed while it travelled.");

        foreach (var observer in source.Players.Values)
            observer.UntrackEntity(this.EntityId);
    }

    private void RelocateWithinLevel(ILevel level, VectorD position, Angle? yaw, Angle? pitch, Action? arrived)
    {
        using (TransferLockOf(level).Enter())
        {
            this.ValidateTravelResident(level);
            var from = this.Position;
            if (level is AbstractLevel resident && !resident.TryMoveEntity(this, from, position))
                throw new InvalidOperationException("The destination level could not accept the entity.");

            this.Place(from, position, yaw ?? this.Yaw, pitch ?? this.Pitch);
            arrived?.Invoke();
        }
    }

    // Where the entity was, and how it faced, before it left a level, to send it back there.
    private readonly record struct Departure(ILevel Level, VectorD Position, Angle Yaw, Angle Pitch);

    private enum Arrival
    {
        Arrived,
        Refused,
        Removed
    }

    // The source's half of a handoff, under its transfer lock: the entity leaves its regions, players and riding
    // links, takes its new place, and names the destination as its level, which owns it from then on.
    private Departure Depart(ILevel source, ILevel destination, VectorD position, Angle? yaw, Angle? pitch)
    {
        using (TransferLockOf(source).Enter())
        {
            this.ValidateTravelResident(source);

            // Riding links never span levels: whoever boarded since the caller took the riders off stays behind.
            this.LeaveVehicle();
            this.EjectPassengers();

            var departure = new Departure(source, this.Position, this.Yaw, this.Pitch);
            ((AbstractLevel)source).RemoveEntity(this, transferring: true);
            if (this is IPlayer player)
                source.TryRemovePlayer(player);

            this.Place(departure.Position, position, yaw ?? this.Yaw, pitch ?? this.Pitch);
            this.removedOnArrival = false;
            this.arrivingAt = destination;
            this.Level = destination;
            return departure;
        }
    }

    // The receiving level's half of a handoff, under its transfer lock. When the level can't take the entity in, it
    // hands the entity back toward where it left (when returning says where), which takes it in the same way.
    private Arrival Arrive(ILevel level, Action? arrived, Departure? returning)
    {
        using (TransferLockOf(level).Enter())
        {
            // Taken or killed on the way: picked up, say, from a lookup that found it before it left.
            var removed = this.removedOnArrival
                || this is ItemEntity { Removed: true }
                || this is Mob { IsRemoved: true };
            this.arrivingAt = null;
            this.removedOnArrival = false;
            if (removed)
            {
                // The rest of a removal, whose most was done as the entity left its source.
                if (this is not IPlayer)
                    API.Data.AttachmentStorage.For(this).Retire();

                return Arrival.Removed;
            }

            if (level.TryAddEntity(this))
            {
                if (this is IPlayer player)
                    level.TryAddPlayer(player);

                arrived?.Invoke();
                return Arrival.Arrived;
            }

            if (returning is Departure back)
            {
                this.Place(this.Position, back.Position, back.Yaw, back.Pitch);
                this.arrivingAt = back.Level;
                this.Level = back.Level;
            }

            return Arrival.Refused;
        }
    }

    private void Place(VectorD from, VectorD position, Angle yaw, Angle pitch)
    {
        this.LastPosition = from;
        this.Position = position;
        this.Yaw = yaw;
        this.Pitch = pitch;
        this.BoundingBox = this.Dimension.CreateBBFromPosition(position);
    }

    internal static void ValidateTravel(ILevel level, VectorD position, Angle? yaw, Angle? pitch)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z)
            || Math.Abs(position.X) >= LevelPortals.WorldBorderLimit
            || Math.Abs(position.Z) >= LevelPortals.WorldBorderLimit
            || position.Y <= int.MinValue || position.Y >= int.MaxValue
            || yaw is Angle y && !float.IsFinite(y.Degrees)
            || pitch is Angle p && !float.IsFinite(p.Degrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position), "Travel requires finite coordinates inside the global coordinate limits.");
        }
    }
}
