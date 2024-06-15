namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeCodec : ICodec
{
    public required string Name { get; set; }

    public required int Id { get; set; }

    public required BiomeElement Element { get; set; }
}
