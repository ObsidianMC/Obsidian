using Obsidian.Nbt;

namespace Obsidian.Utilities.Registry.Codecs.Biomes;

public class BiomeEffect
{
    public BiomeMusicEffect Music { get; set; }

    public string GrassColorModifier { get; set; }
    public string AmbientSound { get; set; }

    public BiomeAdditionSound AdditionsSound { get; set; }
    public BiomeMoodSound MoodSound { get; set; }

    public BiomeParticle Particle { get; set; }

    public int FoliageColor { get; set; }
    public int SkyColor { get; set; }
    public int WaterFogColor { get; set; }
    public int FogColor { get; set; }
    public int WaterColor { get; set; }
    public int GrassColor { get; set; }

    public void Write(NbtCompound compound)
    {
        var effects = new NbtCompound("effects")
            {
                new NbtTag<int>("fog_color", this.FogColor),
                new NbtTag<int>("sky_color", this.SkyColor),
                new NbtTag<int>("water_color", this.WaterColor),
                new NbtTag<int>("water_fog_color", this.WaterFogColor)
            };

        if (this.FoliageColor > 0)
            effects.Add(new NbtTag<int>("foliage_color", this.FoliageColor));

        if (this.GrassColor > 0)
            effects.Add(new NbtTag<int>("grass_color", this.GrassColor));

        if (!this.GrassColorModifier.IsNullOrEmpty())
            effects.Add(new NbtTag<string>("grass_color_modifier", this.GrassColorModifier));

        if (this.AdditionsSound != null)
            this.AdditionsSound.Write(effects);

        if (this.MoodSound != null)
            this.MoodSound.Write(effects);

        if (!this.AmbientSound.IsNullOrEmpty())
            effects.Add(new NbtTag<string>("ambient_sound", this.AmbientSound));

        if (this.Particle != null)
            this.Particle.Write(compound);

        compound.Add(effects);
    }
}
