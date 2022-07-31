namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeCodec : ICodec
{
    public string Name { get; set; }

    public int Id { get; set; }

    public BiomeElement Element { get; set; }
}
