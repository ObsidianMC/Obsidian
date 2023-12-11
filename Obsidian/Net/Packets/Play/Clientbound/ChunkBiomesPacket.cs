using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public sealed partial class ChunkBiomesPacket : IClientboundPacket
{
    [Field(0)]
    public required List<ChunkBiome> ChunkBiomes { get; init; }
         
    public int Id => 0x0E;
}

public readonly struct ChunkBiome
{
    public required int X { get; init; }

    public required int Z { get; init; }

    public required byte[] Data { get; init; }
}
