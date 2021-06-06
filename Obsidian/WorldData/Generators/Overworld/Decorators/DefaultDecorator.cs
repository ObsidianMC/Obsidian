using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class DefaultDecorator : BaseDecorator
    {
        public DefaultDecorator(Biomes biome) : base(biome)
        {
        }

        public override void Decorate(Chunk chunk, Vector pos, BaseBiomeNoise noise)
        {
        }
    }
}
