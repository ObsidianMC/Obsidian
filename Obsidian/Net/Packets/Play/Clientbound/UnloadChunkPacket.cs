using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UnloadChunkPacket : IClientboundPacket
{
    [Field(0)]
    public int X { get; }

    [Field(1)]
    public int Z { get; }

    public int Id => 0x21;

    public UnloadChunkPacket(int x, int z)
    {
        X = x;
        Z = z;
    }
}
