using Obsidian.ChunkData;
using Obsidian.API;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class DefaultDecorator : BaseDecorator
    {
        public DefaultDecorator(Biomes biome) : base(biome)
        {
        }

        public override void Decorate(Chunk chunk, Position pos, OverworldNoise noise)
        {
        }
    }
}
