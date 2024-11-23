using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ForgetLevelChunkPacket(int x, int z)
{
    [Field(0)]
    public int X { get; } = x;

    [Field(1)]
    public int Z { get; } = z;

    public override void Serialize(INetStreamWriter writer)
    {
        //Note: The order is inverted, because the client reads this packet as one big-endian Long, with Z being the upper 32 bits.
        writer.WriteInt(this.Z); 
        writer.WriteInt(this.X);
    }
}
