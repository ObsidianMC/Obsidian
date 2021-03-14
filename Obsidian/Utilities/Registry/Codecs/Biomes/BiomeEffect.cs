using Obsidian.Nbt.Tags;
using Obsidian.Utilities;

namespace Obsidian.Utilities.Registry.Codecs.Biomes
{
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
                new NbtInt("fog_color", this.FogColor),
                new NbtInt("sky_color", this.SkyColor),
                new NbtInt("water_color", this.WaterColor),
                new NbtInt("water_fog_color", this.WaterFogColor)
            };

            if (this.FoliageColor > 0)
                effects.Add(new NbtInt("foliage_color", this.FoliageColor));

            if (this.GrassColor > 0)
                effects.Add(new NbtInt("grass_color", this.GrassColor));

            if (!this.GrassColorModifier.IsNullOrEmpty())
                effects.Add(new NbtString("grass_color_modifier", this.GrassColorModifier));

            if (this.Particle != null)
                this.Particle.Write(effects);

            if (this.AdditionsSound != null)
                this.AdditionsSound.Write(effects);

            if (this.MoodSound != null)
                this.MoodSound.Write(effects);

            if (!this.AmbientSound.IsNullOrEmpty())
                effects.Add(new NbtString("ambient_sound", this.AmbientSound));

            compound.Add(effects);
        }
    }
}
