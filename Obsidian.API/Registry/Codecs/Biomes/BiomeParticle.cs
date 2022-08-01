namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeParticle
{
    public float Probability { get; set; }

    public BiomeOption Options { get; set; }
}
