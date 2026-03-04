using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeEffect : INbtSerializable
{
    public string? GrassColorModifier { get; set; }
    public string? AmbientSound { get; set; }

    public BiomeParticle? Particle { get; set; }

    public string SkyColor { get; set; }
    public string WaterFogColor { get; set; }
    public string FogColor { get; set; }
    public string WaterColor { get; set; }
    public string FoliageColor { get; set; }
    public string GrassColor { get; set; }

    public void Write(INbtWriter writer)
    {
        writer.WriteCompoundStart("effects");

        writer.WriteString("fog_color", this.FogColor);
        writer.WriteString("sky_color", this.SkyColor);
        writer.WriteString("water_color", this.WaterColor);
        writer.WriteString("water_fog_color", this.WaterFogColor);

        if (!string.IsNullOrEmpty(this.FoliageColor))
            writer.WriteString("foliage_color", FoliageColor);

        if (!string.IsNullOrEmpty(this.GrassColor))
            writer.WriteString("grass_color", this.GrassColor);

        if (!this.GrassColorModifier.IsNullOrEmpty())
            writer.WriteString("grass_color_modifier", this.GrassColorModifier);

        //this.AdditionsSound?.Write(writer);
        //this.MoodSound?.Write(writer);
        // Not sure if this will be added back, but it was removed in 1.21.11, so I will leave it out for now.
        //if(this.Music is not null)
        //{
        //    writer.WriteListStart("music", NbtTagType.Compound, this.Music.Length);

        //    foreach (var musicData in this.Music)
        //    {
        //        writer.WriteCompoundStart();

        //        musicData.Write(writer);

        //        writer.EndCompound();
        //    }

        //    writer.EndList();
        //}

        if (!this.AmbientSound.IsNullOrEmpty())
            writer.WriteString("ambient_sound", this.AmbientSound);

        this.Particle?.Write(writer);

        writer.EndCompound();
    }
}
