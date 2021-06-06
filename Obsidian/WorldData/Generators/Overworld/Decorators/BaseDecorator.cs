using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public abstract class BaseDecorator : IDecorator
    {
        protected Biomes biome;

        protected BaseDecorator(Biomes biome)
        {
            this.biome = biome;
        }

        public abstract void Decorate(Chunk chunk, Vector position, BaseBiomeNoise noise);
    }
}
