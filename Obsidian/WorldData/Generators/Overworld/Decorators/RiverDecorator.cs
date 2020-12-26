using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Util.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class RiverDecorator : BaseDecorator
    {

        public RiverDecorator(Biomes biome) : base(biome)
        {
        }

        public override void Decorate(Chunk chunk, Position pos, OverworldNoise noise)
        {
            if (pos.Y < 65)
                chunk.SetBlock(pos, Registry.GetBlock(Material.Gravel));
        }
    }
}
