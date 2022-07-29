namespace Obsidian.API.Registry.Codecs.Biomes;

public class BiomeEffect
{
    public BiomeMusicEffect Music { get; set; }

    public string GrassColorModifier { get; set; }
    public string AmbientSound { get; set; }

    public BiomeAdditionSound AdditionsSound { get; set; }
    public BiomeMoodSound MoodSound { get; set; }

    public BiomeParticle Particle { get; set; }

    public int SkyColor { get; set; }
    public int WaterFogColor { get; set; }
    public int FogColor { get; set; }
    public int WaterColor { get; set; }
    public int FoliageColor { get; set; }
    public int GrassColor { get; set; }
}
