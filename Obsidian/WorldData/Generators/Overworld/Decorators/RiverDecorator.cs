using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class RiverDecorator : BaseDecorator
    {

        public RiverDecorator(Biomes biome) : base(biome)
        {
        }

        public override void Decorate(Chunk chunk, Vector pos, BaseBiomeNoise noise)
        {
            if (pos.Y < 65)
                chunk.SetBlock(pos, Registry.GetBlock(Material.Gravel));
        }
    }
}
