using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Replaces the biomes of chunks a client has, like vanilla's <c>ClientboundChunksBiomesPacket</c>, which
/// <c>/fillbiome</c> sends each player for the changed chunks they have. A client ignores a chunk it doesn't have.
/// </summary>
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

    /// <summary>
    /// Every section's biomes from the bottom up, written as a chunk packet writes them (see
    /// <see cref="LevelChunkWithLightPacket.WriteBiomes"/>).
    /// </summary>
    public required ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>
    /// The chunk's current biomes. Read under its block lock, which its chunk packets are written under, so that both
    /// describe the same biomes.
    /// </summary>
    internal static ChunkBiome Read(IChunk chunk)
    {
        using var buffer = new NetworkBuffer();
        foreach (var section in chunk.Sections)
            LevelChunkWithLightPacket.WriteBiomes(buffer, section.BiomeContainer);

        return new ChunkBiome
        {
            X = chunk.X,
            Z = chunk.Z,
            Data = buffer.AsSpan(0, buffer.Size).ToArray()
        };
    }
}
