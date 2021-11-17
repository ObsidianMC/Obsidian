using Obsidian.Nbt;

namespace Obsidian.Utilities.Registry.Codecs.Biomes;

public class BiomeParticle
{
    public float Probability { get; set; }

    public BiomeOption Options { get; set; }

    internal void Write(NbtCompound compound)
    {
        var particle = new NbtCompound("particle")
            {
                new NbtTag<float>("probability", this.Probability)
            };

        if (this.Options != null)
        {
            var options = new NbtCompound("options")
                {
                    new NbtTag<string>("type", this.Options.Type)
                };

            particle.Add(options);
        }

    }
}
