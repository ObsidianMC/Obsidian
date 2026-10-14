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
            ? await PortalArea.LoadAsync(destination, (Vector)position.Floor(), 0, () => this.Level == source)
            : null;
        if (level is AbstractLevel && area is null)
            throw new InvalidOperationException("The entity moved while the destination was loading.");

        var riders = this is Mob mob ? mob.TravelPassengers.ToArray() : [];
        foreach (var rider in riders)
            rider.Vehicle?.Dismount(rider);

        if (source != level)
        {
            await source.DestroyEntityAsync(this);
            if (this is IPlayer player)
                source.TryRemovePlayer(player);

            foreach (var observer in source.Players.Values.OfType<Player>())
                observer.TrackedEntities.TryRemove(this.EntityId, out _);
        }

        this.LastPosition = from;
        this.Position = position;
        this.Yaw = yaw ?? this.Yaw;
        this.Pitch = pitch ?? this.Pitch;
        this.BoundingBox = this.Dimension.CreateBBFromPosition(position);
        this.Level = level;
        if (source == level && level is AbstractLevel resident)
        {
            if (!resident.TryMoveEntity(this, from, position))
            {
                this.Position = from;
                this.BoundingBox = this.Dimension.CreateBBFromPosition(from);
                throw new InvalidOperationException("The destination region could not accept the entity.");
            }
        }
        else if (!level.TryAddEntity(this))
        {
            this.Level = source;
            this.Position = from;
            this.BoundingBox = this.Dimension.CreateBBFromPosition(from);
            source.TryAddEntity(this);
            if (this is IPlayer player)
                source.TryAddPlayer(player);

            throw new InvalidOperationException("The destination level could not accept the entity.");
        }

        if (this is IPlayer movedPlayer)
            level.TryAddPlayer(movedPlayer);

        if (source != level)
            this.SpawnEntity();
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

        if (this is Mob vehicle)
        {
            foreach (var rider in riders)
            {
                await rider.TeleportAsync(level, position + (rider.Position - from), rider.Yaw, rider.Pitch);
                vehicle.RestoreTravelPassenger(rider);
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
