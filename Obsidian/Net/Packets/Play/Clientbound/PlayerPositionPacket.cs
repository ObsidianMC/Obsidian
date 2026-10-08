using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;


public partial class PlayerPositionPacket
{
    [Field(0), VarLength]
    public int TeleportId { get; init; }

    [Field(1), DataFormat(typeof(double))]
    public VectorD Position { get; init; }

    [Field(2)]
    public VectorD Delta { get; init; }

    [Field(2), DataFormat(typeof(float))]
    public Angle Yaw { get; init; }

    [Field(3), DataFormat(typeof(float))]
    public Angle Pitch { get; init; }

    [Field(4)]
    public PositionFlags Flags { get; init; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

    // Portal transitions need the 9-bit vanilla mask, which the legacy sbyte PositionFlags API cannot represent.
    internal int? RelativeFlags { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.TeleportId);

        VectorD.Write(this.Position, writer);
        VectorD.Write(this.Delta, writer);
        writer.WriteSingle(this.Yaw);
        writer.WriteSingle(this.Pitch);

        writer.WriteInt(this.RelativeFlags ?? (int)this.Flags);
    }
}
