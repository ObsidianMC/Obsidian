using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;


public partial class PlayerPositionPacket
{
    [Field(0), VarLength]
    public int TeleportId { get; init; }

    [Field(1), DataFormat(typeof(double))]
    public VectorF Position { get; init; }

    [Field(2)]
    public VectorF Delta { get; init; }

    [Field(2), DataFormat(typeof(float))]
    public Angle Yaw { get; init; }

    [Field(3), DataFormat(typeof(float))]
    public Angle Pitch { get; init; }

    [Field(4)]
    public PositionFlags Flags { get; init; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.TeleportId);

        writer.WriteAbsolutePositionF(this.Position);
        writer.WriteAbsolutePositionF(this.Delta);
        writer.WriteFloat(this.Yaw);
        writer.WriteFloat(this.Pitch);

        writer.WriteInt(this.Flags);
    }
}
