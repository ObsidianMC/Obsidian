namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Sets an entity's absolute position and rotation, for moves too large for a relative-move packet.
/// </summary>
public partial class EntityPositionSyncPacket
{
    public int EntityId { get; init; }

    public VectorD Position { get; init; }

    public float Yaw { get; init; }

    public float Pitch { get; init; }

    public bool OnGround { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);
        EntityMoveDelta.WritePath(writer, this.Position);
        writer.WriteSingle(this.Yaw);
        writer.WriteSingle(this.Pitch);
        writer.WriteBoolean(this.OnGround);
    }
}
