using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class Entity
{
    /// <summary>Moves out of a growing block immediately, preserving velocity and correcting connected clients.</summary>
    internal void LiftForBlockChange(double distance)
    {
        var offset = new VectorD(0, distance, 0);
        this.Position += offset;
        this.BoundingBox = new BoundingBox(this.BoundingBox.Min + offset, this.BoundingBox.Max + offset);
        this.BroadcastTeleport(this.Position, this.Yaw, this.Pitch, this.MovementFlags);

        if (this is PlayerBase player)
        {
            player.SendPacket(new PlayerPositionPacket
            {
                Position = this.Position,
                Delta = this.Motion,
                Yaw = this.Yaw,
                Pitch = this.Pitch,
                TeleportId = player.TeleportId = Globals.Random.Next(0, 999)
            });
        }
    }
}
