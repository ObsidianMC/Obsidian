namespace Obsidian.Entities;

public partial class Entity
{
    /// <summary>Moves out of a growing block immediately, preserving velocity and correcting connected clients.</summary>
    internal void LiftForBlockChange(double distance)
    {
        var offset = new VectorD(0, distance, 0);
        if (this is PlayerBase player)
        {
            // The chunk tick holds TickLock. Registration and queueing are synchronous under the teleport gate,
            // which never acquires TickLock; do not wait for network work here. Keep the existing velocity.
            _ = player.SendPositionAsync(this.Motion, position: this.Position + offset);
        }
        else
        {
            this.Position += offset;
            this.BoundingBox = new BoundingBox(this.BoundingBox.Min + offset, this.BoundingBox.Max + offset);
        }

        this.BroadcastTeleport(this.Position, this.Yaw, this.Pitch, this.MovementFlags);
    }
}
