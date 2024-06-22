using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateEntityPositionAndRotationPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1), DataFormat(typeof(short))]
    public Vector Delta { get; init; }

    [Field(2)]
    public Angle Yaw { get; init; }

    [Field(3)]
    public Angle Pitch { get; init; }

    [Field(4)]
    public bool OnGround { get; init; }

    public int Id => 0x2F;
}
