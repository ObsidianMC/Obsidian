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
            // Vanilla's ChunkPos.pack as a long: z in the high half, so z comes first.
            writer.WriteInt(chunkBiome.Z);
            writer.WriteInt(chunkBiome.X);

            writer.WriteVarInt(chunkBiome.Data.Length);
            writer.WriteByteArray(chunkBiome.Data.Span);
        }
    }
}

public readonly record struct ChunkBiome
{
    public required int X { get; init; }

    public required int Z { get; init; }

    public required ReadOnlyMemory<byte> Data { get; init; }
}
