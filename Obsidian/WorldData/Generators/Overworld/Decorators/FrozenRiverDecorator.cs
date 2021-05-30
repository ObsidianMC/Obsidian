using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class FrozenRiverDecorator : BaseDecorator
    {
        public FrozenRiverDecorator(Biomes biome) : base(biome)
        {
        }

        public override void Decorate(Chunk chunk, Vector pos, OverworldNoise noise)
        {
            if (pos.Y == 61) // rivers at sea level
            {
                chunk.SetBlock(new Vector(pos.X, 61, pos.Z), Registry.GetBlock(Material.FrostedIce)); // TODO: this is mega broken
            }
        }
    }
}
