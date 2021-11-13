using Obsidian.API;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public abstract class BaseDecorator : IDecorator
    {
        public DecoratorFeatures Features { get; }

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

            Features = new DecoratorFeatures();
        }

        public abstract void Decorate();

        protected void FillWater()
        {
            if (chunk is null) { return; }
            var water = Registry.GetBlock(Material.Water);
            var sand = Registry.GetBlock(Material.Sand);
            if (pos.Y <= noise.settings.WaterLevel)
            {
                chunk.SetBlock(pos, sand);
                for (int y = noise.settings.WaterLevel; y > pos.Y; y--)
                {
                    chunk.SetBlock(pos.X, y, pos.Z, water);
                }
            }
        }

        protected void FillSand()
        {
            if (chunk is null) { return; }
            var sand = Registry.GetBlock(Material.Sand);
            if (pos.Y <= noise.settings.WaterLevel)
            {
                chunk.SetBlock(pos, sand);
            }
        }
    }
}
