using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class FlowerForestDecorator : BaseDecorator
    {
        public FlowerForestDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
            Features.Trees.Add(new DecoratorFeatures.TreeInfo(3, typeof(OakTree)));
            Features.Trees.Add(new DecoratorFeatures.TreeInfo(1, typeof(BirchTree)));
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
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(9));

            if (noise.Decoration(worldX * 0.1, 6, worldZ * 0.1) > 0.8)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.SweetBerryBush));

            if (noise.Decoration(worldX * 0.1, 7, worldZ * 0.1) > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.TallGrass));

            var poppyNoise = noise.Decoration(worldX * 0.1, 9, worldZ * 0.1); // 0.1 makes more groupings
            if (poppyNoise > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Poppy));

            var dandyNoise = noise.Decoration(worldX * 0.1, 10, worldZ * 0.1); // 0.1 makes more groupings
            if (dandyNoise > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Dandelion));

            var cornFlowerNoise = noise.Decoration(worldX * 0.1, 11, worldZ * 0.1); // 0.1 makes more groupings
            if (cornFlowerNoise > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Cornflower));

            var azureNoise = noise.Decoration(worldX * 0.1, 12, worldZ * 0.1); // 0.1 makes more groupings
            if (azureNoise > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.AzureBluet));

            var peonies = noise.Decoration(worldX * 0.1, 13, worldZ * 0.1); // 0.1 makes more groupings
            if (peonies > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Peony));

            if (noise.Decoration(worldX * 0.1, 14, worldZ * 0.1) > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.WhiteTulip));
            if (noise.Decoration(worldX * 0.1, 15, worldZ * 0.1) > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.RedTulip));
            if (noise.Decoration(worldX * 0.1, 16, worldZ * 0.1) > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.OrangeTulip));
            if (noise.Decoration(worldX * 0.1, 17, worldZ * 0.1) > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Allium));
            if (noise.Decoration(worldX * 0.1, 18, worldZ * 0.1) > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.LilyOfTheValley));
            if (noise.Decoration(worldX * 0.1, 19, worldZ * 0.1) > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.BlueOrchid));
            if (noise.Decoration(worldX * 0.1, 19, worldZ * 0.1) > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Lilac));
            if (noise.Decoration(worldX * 0.1, 19, worldZ * 0.1) > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.RoseBush));
        }
    }
}
