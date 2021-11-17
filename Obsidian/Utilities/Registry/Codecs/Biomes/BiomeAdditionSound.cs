using Obsidian.Nbt;

namespace Obsidian.Utilities.Registry.Codecs.Biomes;

public class BiomeAdditionSound
{
    public string Sound { get; set; }

    public double TickChance { get; set; }

    internal void Write(NbtCompound compound)
    {
        var additions = new NbtCompound("additions_sound")
            {
                new NbtTag<string>("sound", this.Sound),
                new NbtTag<double>("tick_chance", this.TickChance)
            };

        compound.Add(additions);
    }
}

public class BiomeMoodSound
{
    public string Sound { get; set; }

    public double Offset { get; set; }

    public int TickDelay { get; set; }
    public int BlockSearchExtent { get; set; }

    internal void Write(NbtCompound compound)
    {
        var mood = new NbtCompound("mood_sound")
            {
                new NbtTag<string>("sound", this.Sound),

                new NbtTag<double>("offset", this.Offset),

                new NbtTag<int>("tick_delay", this.TickDelay),
                new NbtTag<int>("block_search_extent", this.BlockSearchExtent)
            };
    }
}
