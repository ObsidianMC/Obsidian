namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeEffect
{
    public required BiomeMoodSound MoodSound { get; set; }

    public string? GrassColorModifier { get; set; }
    public string? AmbientSound { get; set; }

    public BiomeMusicEffectData[]? Music { get; set; }
    public BiomeAdditionSound? AdditionsSound { get; set; }
    public BiomeParticle? Particle { get; set; }

    public int SkyColor { get; set; }
    public int WaterFogColor { get; set; }
    public int FogColor { get; set; }
    public int WaterColor { get; set; }
    public int FoliageColor { get; set; }
    public int GrassColor { get; set; }
}
