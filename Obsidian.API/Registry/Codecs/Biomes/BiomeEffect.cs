using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeEffect : INbtSerializable
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

    public void Write(INbtWriter writer)
    {
        writer.WriteCompoundStart("effects");

        writer.WriteInt("fog_color", this.FogColor);
        writer.WriteInt("sky_color", this.SkyColor);
        writer.WriteInt("water_color", this.WaterColor);
        writer.WriteInt("water_fog_color", this.WaterFogColor);

        if (this.FoliageColor > 0)
            writer.WriteInt("foliage_color", FoliageColor);

        if (this.GrassColor > 0)
            writer.WriteInt("grass_color", this.GrassColor);

        if (!this.GrassColorModifier.IsNullOrEmpty())
            writer.WriteString("grass_color_modifier", this.GrassColorModifier);

        this.AdditionsSound?.Write(writer);
        this.MoodSound?.Write(writer);

        if(this.Music is not null)
        {
            writer.WriteListStart("music", NbtTagType.Compound, this.Music.Length);

            foreach (var musicData in this.Music)
            {
                writer.WriteCompoundStart();

                musicData.Write(writer);

                writer.EndCompound();
            }

            writer.EndList();
        }

        if (!this.AmbientSound.IsNullOrEmpty())
            writer.WriteString("ambient_sound", this.AmbientSound);

        this.Particle?.Write(writer);

        writer.EndCompound();
    }
}
