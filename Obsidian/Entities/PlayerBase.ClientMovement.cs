using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    public override ValueTask UpdateAsync(VectorD position, MovementFlags movementFlags) =>
        this.ApplyPlayerMovementAsync(position, null, null, movementFlags, false);

    public override ValueTask UpdateAsync(VectorD position, float yaw, float pitch, MovementFlags movementFlags) =>
        this.ApplyPlayerMovementAsync(position, yaw, pitch, movementFlags, false);

    public override ValueTask UpdateAsync(float yaw, float pitch, MovementFlags movementFlags) =>
        this.ApplyPlayerMovementAsync(null, yaw, pitch, movementFlags, false);

    /// <summary>
    /// Applies a movement packet. The rotation, when it has one, is kept as the exact floats the client sent, after
    /// <see cref="ClientRotation"/>; a packet with a rotation that isn't finite is ignored.
    /// </summary>
    internal ValueTask HandleClientMovementAsync(
        VectorD? position, float? yaw, float? pitch, MovementFlags flags)
    {
        if (yaw is not float requestedYaw || pitch is not float requestedPitch)
            return this.ApplyPlayerMovementAsync(position, null, null, flags, true);

        if (!float.IsFinite(requestedYaw) || !float.IsFinite(requestedPitch))
            return default;

        var (clientYaw, clientPitch) = ClientRotation(requestedYaw, requestedPitch);
        return this.ApplyPlayerMovementAsync(position, clientYaw, clientPitch, flags, true);
    }

    /// <summary>
    /// The rotation a player takes from a client's request, as vanilla's packet handler applies it: the yaw wrapped
    /// into [-180, 180), and the pitch wrapped, then clamped to [-90, 90]. The arithmetic is vanilla's so the
    /// stored floats, and every direction aimed by them, are bit for bit vanilla's.
    /// </summary>
    internal static (float Yaw, float Pitch) ClientRotation(float yaw, float pitch)
    {
        var wrappedYaw = Mth.WrapDegrees(yaw) % 360f;
        var clampedPitch = Math.Clamp(Mth.WrapDegrees(pitch), -90f, 90f) % 360f;
        return (wrappedYaw, clampedPitch);
    }

    private async ValueTask ApplyPlayerMovementAsync(
        VectorD? position, float? yaw, float? pitch, MovementFlags flags, bool updateChunks,
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
                if (yaw is float waitingYaw && pitch is float waitingPitch)
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

                if (yaw is float newYaw && pitch is float newPitch)
                    this.BroadcastMovement(destination, newYaw, newPitch, flags);
                else
                    this.BroadcastMovement(destination, flags);

                if (chunk is { IsGenerated: true })
                    this.Position = destination;

                if (this.Dimension != EntityDimension.Zero)
                    this.BoundingBox = this.Dimension.CreateBBFromPosition(destination);
            }
            else if (yaw is float newYaw && pitch is float newPitch)
            {
                this.ApplyPlayerRotation(newYaw, newPitch, flags);
            }

            if (yaw is float rotationYaw && pitch is float rotationPitch)
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

    // Keeps the exact rotation, and tells watchers only when the turn shows in the 256ths of a turn they see.
    private void ApplyPlayerRotation(float yaw, float pitch, MovementFlags flags)
    {
        if (!this.TurnsVisibly(yaw, pitch))
        {
            this.UpdatePosition(yaw, pitch, flags);
            return;
        }

        this.SetRotation(yaw, pitch, flags);
        this.SetHeadRotation(yaw);
    }
}
