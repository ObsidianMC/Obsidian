using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

// Source: https://wiki.vg/Protocol#Set_Center_Chunk
public partial class SetChunkCacheCenterPacket(int chunkX, int chunkZ)
{
    [Field(0), VarLength]
    public int ChunkX { get; } = chunkX;

    [Field(1), VarLength]
    public int ChunkZ { get; } = chunkZ;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.ChunkX);
        writer.WriteVarInt(this.ChunkZ);
    }
}
