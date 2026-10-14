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
        var from = this.Position;
        using var area = level is AbstractLevel destination
            ? await PortalArea.LoadAsync(destination, (Vector)position.Floor(), 0, () => this.IsTravelResident(source))
            : null;
        if (level is AbstractLevel && area is null)
            throw new InvalidOperationException("The entity moved while the destination was loading.");

        Entity[] riders;
        Angle fromYaw;
        Angle fromPitch;
        lock (ItemEntity.TransferLock)
        {
            this.ValidateTravelResident(source);

            // Like vanilla, an entity teleported on its own gets off its vehicle, and its passengers get off to travel
            // separately and board again at the destination.
            riders = this.DetachPassengersForTravel();
            from = this.Position;
            fromYaw = this.Yaw;
            fromPitch = this.Pitch;

            try
            {
                this.CommitTravel(source, level, position, yaw, pitch);
            }
            catch (InvalidOperationException)
            {
                foreach (var rider in riders)
                    rider.Ride(this, force: true);

                throw;
            }

            if (source != level)
            {
                (this as Mob)?.HoldLeashByUuid();
                this.SpawnEntity();
            }
            else
                this.PacketBroadcaster.QueuePacketToLevel(level, new TeleportEntityPacket
                {
                    EntityId = this.EntityId,
                    Position = position,
                    Delta = this.Motion,
                    Yaw = this.Yaw,
                    Pitch = this.Pitch,
                    OnGround = this.MovementFlags.HasFlag(MovementFlags.OnGround)
                });
        }

        await this.CarryPassengersAsync(riders, from, fromYaw, fromPitch);
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
        lock (ItemEntity.TransferLock)
        {
            this.LeaveVehicle();
            var riders = this.Passengers.ToArray();
            this.EjectPassengers();
            return riders;
        }
    }

    // Membership is authoritative for ordinary removal and unloading saves; item pickup and mob death may mark
    // removal before their dictionary entry is taken out. Read and revalidate under the shared transfer lock.
    private protected bool IsTravelResident(ILevel source)
    {
        lock (ItemEntity.TransferLock)
            return this.Level == source
                && this is not ItemEntity { Removed: true }
                && this is not Mob { IsRemoved: true }
                && source is AbstractLevel origin
                && origin.GetRegionForLocation(this.Position) is IRegion region
                && region.Entities.TryGetValue(this.EntityId, out var entity)
                && ReferenceEquals(entity, this);
    }

    private void ValidateTravelResident(ILevel source)
    {
        if (!this.IsTravelResident(source))
            throw new InvalidOperationException("The entity was removed or moved while the destination was loading.");
    }

    // No awaits or tick-lock acquisition here. Saves take tick lock -> transfer lock -> chunk entity lock, so
    // neither a save nor a removal can observe or interrupt the removal/position/addition commit.
    private protected void CommitTravel(
        ILevel source, ILevel destination, VectorD position, Angle? yaw, Angle? pitch)
    {
        lock (ItemEntity.TransferLock)
        {
            this.ValidateTravelResident(source);
            var from = this.Position;
            var oldYaw = this.Yaw;
            var oldPitch = this.Pitch;
            if (source != destination)
            {
                if (source is not AbstractLevel origin)
                    throw new NotSupportedException("Entity transfers require an Obsidian source level.");

                origin.RemoveEntity(this, transferring: true);
                if (this is IPlayer player)
                    source.TryRemovePlayer(player);
            }

            this.LastPosition = from;
            this.Position = position;
            this.Yaw = yaw ?? this.Yaw;
            this.Pitch = pitch ?? this.Pitch;
            this.BoundingBox = this.Dimension.CreateBBFromPosition(position);
            this.Level = destination;
            var added = source == destination && destination is AbstractLevel resident
                ? resident.TryMoveEntity(this, from, position)
                : destination.TryAddEntity(this);
            if (!added)
            {
                this.Level = source;
                this.Position = from;
                this.Yaw = oldYaw;
                this.Pitch = oldPitch;
                this.BoundingBox = this.Dimension.CreateBBFromPosition(from);
                if (source != destination)
                {
                    source.TryAddEntity(this);
                    if (this is IPlayer player)
                        source.TryAddPlayer(player);
                }

                throw new InvalidOperationException("The destination level could not accept the entity.");
            }

            if (this is IPlayer movedPlayer)
                destination.TryAddPlayer(movedPlayer);

            if (source != destination)
            {
                foreach (var observer in source.Players.Values.OfType<Player>())
                    observer.TrackedEntities.TryRemove(this.EntityId, out _);
            }
        }
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
            throw new ArgumentOutOfRangeException(
                nameof(position), "Travel requires finite coordinates inside the global coordinate limits.");
    }
}
