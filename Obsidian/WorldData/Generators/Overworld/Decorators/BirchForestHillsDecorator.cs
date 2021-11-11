using Obsidian.API;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class BirchForestHillsDecorator : BaseDecorator
    {
        public BirchForestHillsDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
            Features.Trees.Add(new DecoratorFeatures.TreeInfo(6, typeof(BirchTree)));
        }

        public override void Decorate()
        {
            if (pos.Y < noise.settings.WaterLevel)
            {
                FillWater();
                return;
            }

            int worldX = (chunk.X << 4) + pos.X;
            int worldZ = (chunk.Z << 4) + pos.Z;

            var grass = Registry.GetBlock(9);
            var dirt = Registry.GetBlock(Material.Dirt);

            chunk.SetBlock(pos, grass);
            for (int y = -1; y > -4; y--)
                chunk.SetBlock(pos + (0, y, 0), dirt);

            // Flowers
            var grassNoise = noise.Decoration(worldX * 0.1, 8, worldZ * 0.1);
            if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Grass));

            var gravelNoise = noise.Decoration(worldX * 0.06, 9, worldZ * 0.06); // 0.03 makes more groupings
            if (gravelNoise > 1)
                chunk.SetBlock(pos, Registry.GetBlock(Material.Gravel));

            var dandyNoise = noise.Decoration(worldX * 0.06, 10, worldZ * 0.06); // 0.03 makes more groupings
            if (dandyNoise > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Dandelion));
        }
    }
}
