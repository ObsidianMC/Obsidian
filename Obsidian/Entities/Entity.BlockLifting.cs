namespace Obsidian.Entities;

public partial class Entity
{
    /// <summary>Moves out of a growing block immediately, preserving velocity and correcting connected clients.</summary>
    internal void LiftForBlockChange(double distance, BoundingBox storedBox, MovementFlags movementFlags)
    {
        var offset = new VectorD(0, distance, 0);
        if (this is PlayerBase player)
        {
            // The chunk tick holds TickLock. Registration and queueing are synchronous under the teleport gate,
            // which never acquires TickLock; do not wait for network work here. Keep the existing velocity.
            player.SendBlockLift(this.Position + offset, storedBox, offset);
        }
        else
        {
            this.SetBlockLiftPosition(this.Position + offset, storedBox, offset);
        }

        this.BroadcastTeleport(this.Position, this.Yaw, this.Pitch, movementFlags);
    }

    // Geometry was prepared outside transfer; committing it must not dispatch virtual plugin accessors.
    internal void SetBlockLiftPosition(VectorD position, BoundingBox storedBox, VectorD offset)
    {
        this.Position = position;
        this.boundingBox = new BoundingBox(storedBox.Min + offset, storedBox.Max + offset);
    }
}
