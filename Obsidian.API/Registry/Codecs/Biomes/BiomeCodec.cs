namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required BiomeElement Element { get; init; }
}
