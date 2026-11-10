using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    public override ValueTask UpdateAsync(VectorD position, MovementFlags movementFlags) =>
        this.ApplyPlayerMovementAsync(position, null, null, movementFlags, false);

    public override ValueTask UpdateAsync(VectorD position, Angle yaw, Angle pitch, MovementFlags movementFlags) =>
        this.ApplyPlayerMovementAsync(position, yaw, pitch, movementFlags, false);

    public override ValueTask UpdateAsync(Angle yaw, Angle pitch, MovementFlags movementFlags) =>
        this.ApplyPlayerMovementAsync(null, yaw, pitch, movementFlags, false);

    internal ValueTask HandleClientMovementAsync(
        VectorD? position, Angle? yaw, Angle? pitch, MovementFlags flags) =>
        this.ApplyPlayerMovementAsync(position, yaw, pitch, flags, true);

    private async ValueTask ApplyPlayerMovementAsync(
        VectorD? position, Angle? yaw, Angle? pitch, MovementFlags flags, bool updateChunks,
        long? expectedGeneration = null)
    {
        ILevel level;
        long generation;
        ValueTask retry = default;
        bool waiting;
        lock (this.teleportStateGate)
        {
            if (!this.Alive || this.Respawning || this.movementHolds != 0)
                return;
            if (expectedGeneration is long expected && expected != this.movementGeneration)
                return;

            waiting = this.pendingTeleport is not null;
            if (waiting)
            {
                retry = this.RetryTeleportUnderLock();
                // Vanilla still takes the requested rotation while waiting, but no ground/collision flags.
                if (yaw is Angle waitingYaw && pitch is Angle waitingPitch)
                    this.ApplyPlayerRotation(waitingYaw, waitingPitch, this.MovementFlags);
            }

            level = this.Level;
            generation = this.movementGeneration;
        }

        await retry;
        if (waiting)
            return;

        if (position is VectorD requested
            && (!double.IsFinite(requested.X) || !double.IsFinite(requested.Y) || !double.IsFinite(requested.Z)))
        {
            return;
        }

        // The old movement path also only sets position when the destination chunk is generated. Do its async
        // lookup before the commit, then recheck admission: travel could have started while the lookup awaited.
        IChunk? chunk = null;
        if (position is VectorD target)
        {
            var (x, z) = Region.ChunkOf(target);
            chunk = await level.GetChunkAsync(x, z, false);
        }

        VectorD oldPosition;
        MovementFlags oldFlags;
        bool changedChunks;
        lock (this.teleportStateGate)
        {
            if (generation != this.movementGeneration || this.pendingTeleport is not null
                || this.movementHolds != 0 || !this.Alive || this.Respawning || this.Level != level)
            {
                return;
            }

            oldPosition = this.Position;
            oldFlags = this.MovementFlags;
            if (position is VectorD destination)
            {
                if (this.Vehicle is not null)
                    destination = this.Position;

                if (yaw is Angle newYaw && pitch is Angle newPitch)
                    this.BroadcastMovement(destination, newYaw, newPitch, flags);
                else
                    this.BroadcastMovement(destination, flags);

                if (chunk is { IsGenerated: true })
                    this.Position = destination;

                if (this.Dimension != EntityDimension.Zero)
                    this.BoundingBox = this.Dimension.CreateBBFromPosition(destination);
            }
            else if (yaw is Angle newYaw && pitch is Angle newPitch)
            {
                this.ApplyPlayerRotation(newYaw, newPitch, flags);
            }

            if (yaw is Angle rotationYaw && pitch is Angle rotationPitch)
            {
                this.Yaw = rotationYaw;
                this.Pitch = rotationPitch;
            }

            this.MovementFlags = flags;
            changedChunks = this.Position.ToChunkCoord() != this.LastPosition.ToChunkCoord();
            if (updateChunks && position is not null)
                this.LastPosition = this.Position;
        }

        if (position is not null)
        {
            this.PositionPassengers();
            this.RecordProgressMovement(oldPosition, oldFlags);
            await this.UpdateFallAsync(oldPosition, flags);
            if (level is AbstractLevel events)
                await events.EmitMovementGameEventsAsync(this, oldPosition, oldFlags);

            await this.TrySpawnPlayerAsync(this.Position);
        }
        else
        {
            await this.UpdateFallAsync(this.Position, flags);
        }

        if (updateChunks && position is not null && changedChunks)
            await this.UpdateChunksAsync();
    }

    private void ApplyPlayerRotation(Angle yaw, Angle pitch, MovementFlags flags)
    {
        if (yaw == this.Yaw && pitch == this.Pitch)
            return;

        this.SetRotation(yaw, pitch, flags);
        this.SetHeadRotation(yaw);
    }
}
