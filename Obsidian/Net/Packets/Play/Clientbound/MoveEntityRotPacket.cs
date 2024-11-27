using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class MoveEntityRotPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1)]
    public Angle Yaw { get; init; }

    [Field(2)]
    public Angle Pitch { get; init; }

    [Field(3)]
    public bool OnGround { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);

        writer.WriteByte(this.Yaw.Value);
        writer.WriteByte(this.Pitch.Value);

        writer.WriteBoolean(this.OnGround);
    }
}
