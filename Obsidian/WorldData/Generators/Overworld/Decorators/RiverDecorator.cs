using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Util.DataTypes;
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
/*            if (pos.Y > 60) // river above water level
            {
                chunk.SetBlock(pos, Registry.GetBlock(Materials.Water));
                chunk.SetBlock(pos + (0, -1, 0), Registry.GetBlock(Materials.Sand));
            }*/
        }
    }
}
