using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public abstract class BaseDecorator : IDecorator
    {
        protected Biomes biome;

        protected Chunk chunk;

        protected Vector pos;

        protected BaseBiomeNoise noise;

        protected BaseDecorator(Biomes biome, Chunk chunk, Vector pos, BaseBiomeNoise noise)
        {
            this.biome = biome;
            this.chunk = chunk;
            this.pos = pos;
            this.noise = noise;
        }

        public abstract void Decorate();

        protected void FillWater()
        {
            if (chunk is null) { return; }
            var water = Registry.GetBlock(Material.Water);
            if (pos.Y <= noise.settings.WaterLevel)
            {
                for (int y = noise.settings.WaterLevel; y > pos.Y; y--)
                {
                    chunk.SetBlock(pos.X, y, pos.Z, water);
                }
            }
        }
    }
}
