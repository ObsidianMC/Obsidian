using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Util.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class FrozenRiverDecorator : BaseDecorator
    {
        public FrozenRiverDecorator(Biomes biome) : base(biome)
        {
        }

        public override void Decorate(Chunk chunk, Position pos, OverworldNoise noise)
        {
            if (pos.Y == 61) // rivers at sea level
            {
                chunk.SetBlock(new Position(pos.X, 61, pos.Z), Registry.GetBlock(Materials.FrostedIce)); // TODO: this is mega broken
            }
        }
    }
}
