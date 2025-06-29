using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeParticle : INbtSerializable
{
    public required float Probability { get; set; }

    public required BiomeOption Options { get; set; }

    public void Write(INbtWriter writer)
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

        writer.WriteTag(particle);
    }
}
