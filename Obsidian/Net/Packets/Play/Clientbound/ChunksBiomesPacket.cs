using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class ChunksBiomesPacket
{
    [Field(0)]
    public required List<ChunkBiome> ChunkBiomes { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(ChunkBiomes.Count);

        foreach(var chunkBiome in ChunkBiomes)
        {
            writer.WriteInt(chunkBiome.X);
            writer.WriteInt(chunkBiome.Z);

            writer.WriteVarInt(chunkBiome.Data.Length);
            writer.WriteByteArray(chunkBiome.Data);
        }
    }
}

public readonly struct ChunkBiome
{
    public required int X { get; init; }

    public required int Z { get; init; }

    public required byte[] Data { get; init; }
}
