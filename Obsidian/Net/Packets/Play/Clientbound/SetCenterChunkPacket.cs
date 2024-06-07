using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

// Source: https://wiki.vg/Protocol#Set_Center_Chunk
public partial class SetCenterChunkPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int ChunkX { get; }

    [Field(1), VarLength]
    public int ChunkZ { get; }

    public SetCenterChunkPacket(int chunkX, int chunkZ)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
    }

    public int Id => 0x54;
}
