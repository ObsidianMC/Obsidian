using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using System.Threading;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    // Protects movement admission, its synchronous position/broadcast commit, and all pending-teleport state.
    // Chunk lookups, gameplay callbacks and awaits happen outside this lock.
    private readonly Lock teleportStateGate = new();
    private PendingTeleport? pendingTeleport;
    private long movementGeneration;
    private int teleportCounter;
    private long teleportClock;
    private int movementHolds;
    private TaskCompletionSource? movementReleased;

    private sealed record PendingTeleport(int Id, VectorD Position, long Tick, long Generation);

    /// <summary>
    /// The pending teleport's id, or -1 when none is pending. Prefer TeleportAsync to setting an id: the setter is
    /// retained for plugins that already issue their own position packet and registers the current position.
    /// </summary>
    public int TeleportId
    {
        get
        {
            lock (this.teleportStateGate)
                return this.pendingTeleport?.Id ?? AcceptTeleportationPacket.NoTeleportAwaited;
        }
        set
        {
            lock (this.teleportStateGate)
            {
                this.movementGeneration++;
                this.pendingTeleport = value == AcceptTeleportationPacket.NoTeleportAwaited
                    ? null
                    : new(value, this.Position, this.teleportClock, this.movementGeneration);
                if (value >= 0)
                    this.teleportCounter = value;
            }
        }
    }

    internal bool HasPendingTeleport
    {
        get
        {
            lock (this.teleportStateGate)
                return this.pendingTeleport is not null || this.movementHolds != 0;
        }
    }

    // A reservation, not a held lock: travel can await chunks or run callbacks while movement is suspended.
    private void HoldClientMovement()
    {
        lock (this.teleportStateGate)
        {
            this.movementGeneration++;
            if (this.movementHolds++ == 0)
                this.movementReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void ReleaseClientMovement()
    {
        TaskCompletionSource? released = null;
        lock (this.teleportStateGate)
        {
            if (--this.movementHolds == 0)
            {
                released = this.movementReleased;
                this.movementReleased = null;
            }
        }

        released?.TrySetResult();
    }

    private void TickTeleportClock()
    {
        lock (this.teleportStateGate)
            this.teleportClock++;
    }

    /// <summary>
    /// Registers and queues the current server position together. An optional destination commits a direct move
    /// (waking from a bed) in the same critical section. Other travel reserves movement before moving the player.
    /// </summary>
    internal ValueTask SendPositionAsync(VectorD delta, int relativeFlags = 0, VectorD? position = null)
    {
        lock (this.teleportStateGate)
        {
            if (position is VectorD destination)
            {
                this.Position = destination;
                this.BoundingBox = this.Dimension.CreateBBFromPosition(destination);
            }

            this.movementGeneration++;
            return this.QueueTeleportUnderLock(delta, relativeFlags);
        }
    }

    private ValueTask QueueTeleportUnderLock(VectorD delta, int relativeFlags)
    {
        // Vanilla skips int.MaxValue: the counter runs 1..int.MaxValue-1, 0, 1...
        this.teleportCounter = this.teleportCounter >= int.MaxValue - 1 ? 0 : this.teleportCounter + 1;
        this.pendingTeleport = this.Connection is null
            ? null
            : new(this.teleportCounter, this.Position, this.teleportClock, this.movementGeneration);

        // Client.QueuePacketAsync only enqueues; plugins run later in the connection's pipeline.
        // Return its task so the caller can await it after the state lock has been released.
        return this.QueuePacketAsync(new PlayerPositionPacket
        {
            TeleportId = this.teleportCounter,
            Position = this.Position,
            Delta = delta,
            Yaw = this.Yaw,
            Pitch = this.Pitch,
            RelativeFlags = relativeFlags
        });
    }

    private ValueTask RetryTeleportUnderLock()
    {
        if (this.pendingTeleport is not PendingTeleport pending || this.teleportClock - pending.Tick <= 20)
            return default;

        // In 26.3 retrying is driven by movement packets, after more than 20 ticks. Relative velocity and rotation
        // must not be replayed: send the stored absolute position, zero velocity and the current rotation anew.
        this.Position = pending.Position;
        this.Motion = VectorD.Zero;
        this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
        this.movementGeneration++;
        return this.QueueTeleportUnderLock(VectorD.Zero, 0);
    }

    internal async ValueTask ConfirmTeleportAsync(int id, VectorD position, float yaw, float pitch)
    {
        long generation;
        Task? released;
        bool invalid;
        lock (this.teleportStateGate)
        {
            if (id != this.teleportCounter)
                return;

            var pending = this.pendingTeleport;
            invalid = pending is null || double.IsNaN(position.X) || double.IsNaN(position.Y)
                || double.IsNaN(position.Z) || !float.IsFinite(yaw) || !float.IsFinite(pitch);
            if (!invalid && pending!.Generation != this.movementGeneration)
                return;

            generation = this.movementGeneration;
            released = this.movementReleased?.Task;
            if (!invalid)
            {
                this.Position = pending!.Position;
                this.LastPosition = pending.Position;
                this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
                this.pendingTeleport = null;
            }
        }

        if (invalid)
        {
            await this.KickAsync(new ChatMessage { Translate = "multiplayer.disconnect.invalid_player_movement" });
            return;
        }

        if (released is not null)
            await released;

        // 26.3 confirmations report the client's resolved transform. Start from the stored destination, then use
        // the same movement path with ground/collision flags cleared; a newer teleport can invalidate this work.
        var flags = this.MovementFlags & ~(MovementFlags.OnGround | MovementFlags.HorizontalCollision);
        await this.ApplyPlayerMovementAsync(position, yaw, pitch, flags, true, generation);
    }

    internal long CaptureMovementGeneration()
    {
        lock (this.teleportStateGate)
            return this.movementGeneration;
    }

    internal bool CanApplyVehicleMovement(long generation)
    {
        lock (this.teleportStateGate)
        {
            return generation == this.movementGeneration
                && this.pendingTeleport is null && this.movementHolds == 0;
        }
    }

    internal bool PositionFromClientVehicle(Entity vehicle, VectorD position, long generation)
    {
        lock (this.teleportStateGate)
        {
            if (generation != this.movementGeneration || this.pendingTeleport is not null
                || this.movementHolds != 0 || this.Vehicle != vehicle || this.Level != vehicle.Level)
            {
                return true;
            }

            if (this.Level is WorldData.AbstractLevel level && !level.TryMoveEntity(this, this.Position, position))
                return false;

            this.LastPosition = this.Position;
            this.Position = position;
            return true;
        }
    }

    internal async ValueTask EnqueueVehicleMovementAsync(VehicleMove move)
    {
        ValueTask retry = default;
        lock (this.teleportStateGate)
        {
            if (this.movementHolds != 0)
                return;

            if (this.pendingTeleport is not null)
            {
                retry = this.RetryTeleportUnderLock();
            }
            else if (this.Vehicle is PluginMob vehicle)
            {
                vehicle.ClientControl.Enqueue(this, move);
            }
            else if (this.Vehicle is Boat boat)
            {
                boat.ClientControl.Enqueue(this, move);
            }
        }

        await retry;
    }
}
