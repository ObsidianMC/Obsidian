using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

// Source: https://wiki.vg/Protocol#Set_Center_Chunk
public partial class SetChunkCacheCenterPacket
{
    [Field(0), VarLength]
    public int ChunkX { get; }

    [Field(1), VarLength]
    public int ChunkZ { get; }

    public SetChunkCacheCenterPacket(int chunkX, int chunkZ)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
    }
}
