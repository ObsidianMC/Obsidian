using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;
using System;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class MushroomFieldsDecorator : BaseDecorator
    {
        public MushroomFieldsDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
            Features.Trees.Add(new DecoratorFeatures.TreeInfo(1, typeof(OakTree)));
            Features.Trees.Add(new DecoratorFeatures.TreeInfo(1, typeof(AcaciaTree)));
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

            var poppyNoise = noise.Decoration(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
            if (poppyNoise > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Poppy));

            var dandyNoise = noise.Decoration(worldX * 0.03, 10, worldZ * 0.03); // 0.03 makes more groupings
            if (dandyNoise > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Dandelion));

            var cornFlowerNoise = noise.Decoration(worldX * 0.03, 11, worldZ * 0.03); // 0.03 makes more groupings
            if (cornFlowerNoise > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Cornflower));

            var azureNoise = noise.Decoration(worldX * 0.03, 12, worldZ * 0.03); // 0.03 makes more groupings
            if (azureNoise > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.AzureBluet));
        }
    }
}
