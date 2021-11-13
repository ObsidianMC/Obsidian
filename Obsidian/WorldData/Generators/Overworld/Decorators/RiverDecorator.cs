using Obsidian.API;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class RiverDecorator : BaseDecorator
    {

        public RiverDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
        }

        public override void Decorate()
        {
            var sand = Registry.GetBlock(Material.Sand);
            var dirt = Registry.GetBlock(Material.Dirt);
            var gravel = Registry.GetBlock(Material.Gravel);

            FillWater();

            if (pos.Y <= noise.settings.WaterLevel)
            {
                chunk.SetBlock(pos, gravel);
            }
            else
            {
                chunk.SetBlock(pos, sand);
                for (int y = -1; y > -4; y--)
                    chunk.SetBlock(pos + (0, y, 0), gravel);
            }
        }
    }
}
