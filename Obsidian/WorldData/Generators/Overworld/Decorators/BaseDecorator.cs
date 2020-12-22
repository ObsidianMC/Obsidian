using Obsidian.ChunkData;
using Obsidian.API;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public abstract class BaseDecorator : IDecorator
    {
        protected Biomes biome;

        protected BaseDecorator(Biomes biome)
        {
            this.biome = biome;
        }

        public abstract void Decorate(Chunk chunk, Position position, OverworldNoise noise);
    }
}
