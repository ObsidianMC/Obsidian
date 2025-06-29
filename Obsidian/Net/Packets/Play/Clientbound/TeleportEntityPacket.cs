using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class TeleportEntityPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1), DataFormat(typeof(double))]
    public VectorF Position { get; init; }

    [Field(2)]
    public Angle Yaw { get; init; }

    [Field(3)]
    public Angle Pitch { get; init; }

    [Field(5)]
    public PositionFlags Flags { get; init; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

    [Field(6)]
    public bool OnGround { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);

        writer.WriteAbsolutePositionF(this.Position);

        writer.WriteSingle(this.Yaw);
        writer.WriteSingle(this.Pitch);

        writer.WriteInt(this.Flags);

        writer.WriteBoolean(this.OnGround);
    }
}
