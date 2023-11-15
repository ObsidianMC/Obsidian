using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SpawnExperienceOrbPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1), DataFormat(typeof(double))]
    public VectorF Position { get; }

    [Field(2)]
    public short Count { get; }

    public int Id => 0x02;

    public SpawnExperienceOrbPacket(short count, VectorF position)
    {
        Count = count;
        Position = position;
    }
}
