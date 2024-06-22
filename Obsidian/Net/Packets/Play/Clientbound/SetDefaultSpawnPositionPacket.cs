using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetDefaultSpawnPositionPacket : IClientboundPacket
{
    [Field(0)]
    public VectorF Position { get; }

    [Field(1), DataFormat(typeof(float))]
    public Angle Angle { get; set; }

    public int Id => 0x56;

    public SetDefaultSpawnPositionPacket(VectorF position)
    {
        Position = position;
    }
}
