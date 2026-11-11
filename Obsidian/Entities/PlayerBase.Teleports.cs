using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using System.Threading;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    // Protects serverbound player movement admission/commit and pending-teleport state. It does not cover every
    // Entity position writer (such as piston displacement); see docs/known-gaps/movement-and-swing-details.md.
    // Chunk lookups, explicit gameplay callbacks and awaits happen outside this lock. Vehicle commits can still
    // reach virtual Dimension and BoundingBox accessors.
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
    /// Setting this property and sending that packet are not atomic together: another teleport can intervene.
    /// TeleportAsync is the reliable way to register and send a server teleport.
    /// Reusing the currently pending id throws: a confirmation carries only its id, so two pending uses of that id
    /// cannot be distinguished. Plugins must choose fresh ids; -1 clears the pending registration.
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
                if (this.pendingTeleport?.Id == value)
                    throw new InvalidOperationException("The teleport id is already pending; choose a fresh id.");

                this.movementGeneration++;
                this.pendingTeleport = value == AcceptTeleportationPacket.NoTeleportAwaited
                    ? null
                    : new(value, this.Position, this.teleportClock, this.movementGeneration);
                if (value > this.teleportCounter)
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

    internal void TickTeleportClock()
    {
        lock (this.teleportStateGate)
            this.teleportClock++;
    }

    // Disposal must release a confirmation handler even when the travel holding movement never finishes.
    // Invalidate its generation so waking the waiter cannot apply the confirmation after disconnect.
    internal void StopTeleportWaits()
    {
        TaskCompletionSource? released;
        lock (this.teleportStateGate)
        {
            this.movementGeneration++;
            released = this.movementReleased;
        }

        released?.TrySetResult();
    }

    /// <summary>
    /// Registers and queues the current server position together. An optional destination commits a direct move
    /// (waking from a bed) in the same critical section. Other travel reserves movement before moving the player.
    /// </summary>
    internal ValueTask SendPositionAsync(
        VectorD delta, PositionFlags relativeFlags = PositionFlags.None, VectorD? position = null)
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

    private ValueTask QueueTeleportUnderLock(VectorD delta, PositionFlags relativeFlags)
    {
        // Vanilla skips int.MaxValue: the counter runs 1..int.MaxValue-1, 0, 1...
        this.teleportCounter = this.teleportCounter >= int.MaxValue - 1 ? 0 : this.teleportCounter + 1;
        // A plugin can register a lower id without rewinding the counter, including zero just before wrap.
        if (this.pendingTeleport?.Id == this.teleportCounter)
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
            Flags = relativeFlags
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
        return this.QueueTeleportUnderLock(VectorD.Zero, PositionFlags.None);
    }

    internal async ValueTask ConfirmTeleportAsync(int id, VectorD position, float yaw, float pitch)
    {
        var validRotation = TryClientRotation(yaw, pitch, out var rotation);
        long generation;
        Task? released;
        bool invalid;
        lock (this.teleportStateGate)
        {
            var pending = this.pendingTeleport;
            if (id != (pending?.Id ?? this.teleportCounter))
                return;

            invalid = pending is null || double.IsNaN(position.X) || double.IsNaN(position.Y)
                || double.IsNaN(position.Z) || !validRotation;
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

        if (!this.Connected)
            return;

        // 26.3 confirmations report the client's resolved transform. Start from the stored destination, then use
        // the same movement path with ground/collision flags cleared; a newer teleport can invalidate this work.
        var flags = this.MovementFlags & ~(MovementFlags.OnGround | MovementFlags.HorizontalCollision);
        await this.ApplyPlayerMovementAsync(
            ClientPosition(position), rotation.Yaw, rotation.Pitch, flags, true, generation);
    }

    internal long CaptureMovementGeneration()
    {
        lock (this.teleportStateGate)
            return this.movementGeneration;
    }

    internal bool CanApplyVehicleMovement(Entity vehicle, long generation)
    {
        lock (this.teleportStateGate)
        {
            return generation == this.movementGeneration
                && this.pendingTeleport is null && this.movementHolds == 0
                && this.ControlsVehicle(vehicle);
        }
    }

    private bool ControlsVehicle(Entity vehicle) => this.Vehicle == vehicle && this.Level == vehicle.Level
        && ReferenceEquals(vehicle.Passengers.FirstOrDefault(), this);

    internal bool PositionFromVehicle(Entity vehicle, VectorD position, long generation)
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

    // The vehicle's owning-level transfer lock is held, so a riding change cannot separate control validation from
    // commitment. The synchronous host commit still reaches virtual Dimension and BoundingBox accessors.
    internal bool ApplyClientVehicleMovement(Entity vehicle, VehicleMove move, long generation)
    {
        lock (this.teleportStateGate)
        {
            if (generation != this.movementGeneration || this.pendingTeleport is not null
                || this.movementHolds != 0 || !this.ControlsVehicle(vehicle))
            {
                return true;
            }

            return vehicle switch
            {
                Boat boat => boat.CommitClientMovement(move),
                PluginMob mob => mob.CommitClientMovement(move),
                _ => true
            };
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
