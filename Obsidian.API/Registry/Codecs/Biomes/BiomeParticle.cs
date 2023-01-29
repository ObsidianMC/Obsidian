namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeParticle
{
    public required float Probability { get; set; }

    public required BiomeOption Options { get; set; }
}
