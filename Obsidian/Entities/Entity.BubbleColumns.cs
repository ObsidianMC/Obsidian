using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class Entity
{
    /// <summary>
    /// Vanilla's <c>Entity.onAboveBubbleColumn</c>: over a column with open water above, the entity is pushed up (or down
    /// for magma's column). Its motion is sent to clients.
    /// </summary>
    internal virtual void OnAboveBubbleColumn(bool downwards, Vector position)
    {
        var motion = this.Motion;
        var y = downwards ? Math.Max(-0.9, motion.Y - 0.03) : Math.Min(1.8, motion.Y + 0.1);
        this.Motion = new VectorD(motion.X, y, motion.Z);
        this.SendBubbleMotion();
    }

    /// <summary>
    /// Vanilla's <c>Entity.onInsideBubbleColumn</c>: inside a column the entity is pushed up (or down), more gently than
    /// above it, and its fall distance resets.
    /// </summary>
    internal virtual void OnInsideBubbleColumn(bool downwards)
    {
        var motion = this.Motion;
        var y = downwards ? Math.Max(-0.3, motion.Y - 0.03) : Math.Min(0.7, motion.Y + 0.06);
        this.Motion = new VectorD(motion.X, y, motion.Z);
        this.ResetFallDistance();
        this.SendBubbleMotion();
    }

    /// <summary>Vanilla's <c>Entity.resetFallDistance</c>: only entities that track a fall distance override this.</summary>
    internal virtual void ResetFallDistance()
    {
    }

    private void SendBubbleMotion() =>
        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, new SetEntityMotionPacket
        {
            EntityId = this.EntityId,
            Velocity = new(this.Motion.X, this.Motion.Y, this.Motion.Z)
        });
}
