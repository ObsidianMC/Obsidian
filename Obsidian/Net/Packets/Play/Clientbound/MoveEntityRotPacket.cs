namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Turns an entity without moving it.
/// </summary>
public partial class MoveEntityRotPacket
{
    public int EntityId { get; init; }

    public Angle Yaw { get; init; }

    public Angle Pitch { get; init; }

    public bool OnGround { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);

        // Since 26.3 the ground flag comes before the rotation.
        writer.WriteBoolean(this.OnGround);
        writer.WriteByte(this.Yaw.Value);
        writer.WriteByte(this.Pitch.Value);
    }
}
