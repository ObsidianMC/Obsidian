using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SpawnPosition : IClientboundPacket
{
    [Field(0)]
    public VectorF Position { get; }

    [Field(1), DataFormat(typeof(float))]
    public Angle Angle { get; set; }

    public int Id => 0x4B;

    public SpawnPosition(VectorF position)
    {
        Position = position;
    }
}
