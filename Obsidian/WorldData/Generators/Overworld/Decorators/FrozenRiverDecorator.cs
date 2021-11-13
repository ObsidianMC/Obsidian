using Obsidian.API;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class FrozenRiverDecorator : BaseDecorator
    {
        public FrozenRiverDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
        }

        public override void Decorate()
        {
            var sand = Registry.GetBlock(Material.Sand);
            var dirt = Registry.GetBlock(Material.Dirt);
            var gravel = Registry.GetBlock(Material.Gravel);
            var water = Registry.GetBlock(Material.Water);
            var ice = Registry.GetBlock(Material.Ice);

            if (pos.Y <= 64)
            {
                chunk.SetBlock(pos, gravel);
                for (int y = 63; y > pos.Y; y--)
                {
                    chunk.SetBlock(pos.X, y, pos.Z, water);
                }
                chunk.SetBlock(pos.X, 64, pos.Z, ice);
            }
            else
            {
                chunk.SetBlock(pos, sand);
                for (int y = -1; y > -4; y--)
                    chunk.SetBlock(pos + (0, y, 0), sand);
            }
        }
    }
}
