using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ForgetLevelChunkPacket
{
    [Field(0)]
    public int X { get; }

    [Field(1)]
    public int Z { get; }
    public ForgetLevelChunkPacket(int x, int z)
    {
        X = x;
        Z = z;
    }
}
