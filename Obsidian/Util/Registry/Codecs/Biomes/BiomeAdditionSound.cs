using Obsidian.Nbt.Tags;

namespace Obsidian.Util.Registry.Codecs.Biomes
{
    public class BiomeAdditionSound
    {
        public string Sound { get; set; }

        public double TickChance { get; set; }

        internal void Write(NbtCompound compound)
        {
            var additions = new NbtCompound("additions_sound")
            {
                new NbtString("sound", this.Sound),
                new NbtDouble("tick_chance", this.TickChance)
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
                new NbtString("sound", this.Sound),

                new NbtDouble("offset", this.Offset),

                new NbtInt("tick_delay", this.TickDelay),
                new NbtInt("block_search_extent", this.BlockSearchExtent)
            };
        }
    }
}
