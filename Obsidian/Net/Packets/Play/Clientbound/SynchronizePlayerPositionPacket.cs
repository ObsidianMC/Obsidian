using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

[Flags]
public enum PositionFlags : sbyte
{
    X = 0x01,
    Y = 0x02,
    Z = 0x04,
    RotationY = 0x08,
    RotationX = 0x10,
    None = 0x00
}

public partial class SynchronizePlayerPositionPacket : IClientboundPacket
{
    [Field(0), DataFormat(typeof(double))]
    public VectorF Position { get; init; }

    [Field(1), DataFormat(typeof(float))]
    public Angle Yaw { get; init; }

    [Field(2), DataFormat(typeof(float))]
    public Angle Pitch { get; init; }

    [Field(3), ActualType(typeof(sbyte))]
    public PositionFlags Flags { get; init; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

    [Field(4), VarLength]
    public int TeleportId { get; init; }

    public int Id => 0x40;
}
