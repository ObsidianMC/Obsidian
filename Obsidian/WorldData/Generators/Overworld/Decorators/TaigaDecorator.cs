using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class TaigaDecorator : BaseDecorator
    {
        public TaigaDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
            Features.Trees.Add(new DecoratorFeatures.TreeInfo(3, typeof(SpruceTree)));
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

            var grassblock = new Block(Material.GrassBlock, 1);
            var dirt = Registry.GetBlock(Material.Dirt);

            chunk.SetBlock(pos, grassblock);
            for (int y = -1; y > -4; y--)
                chunk.SetBlock(pos + (0, y, 0), dirt);

            var grass = Registry.GetBlock(Material.Grass);
            var dandelion = Registry.GetBlock(Material.Dandelion);

            var bushNoise = noise.Decoration(worldX * 0.1, 0, worldZ * 0.1);
            if (bushNoise > 0 && bushNoise < 0.1) // 5% chance for bush
                chunk.SetBlock(pos + (0, 1, 0), grass);

            var cactusNoise = noise.Decoration(worldX * 0.1, 1, worldZ * 0.1);
            if (cactusNoise > 0 && cactusNoise < 0.05) // 1% chance for cactus
            {
                chunk.SetBlock(pos + (0, 1, 0), dandelion);
            }
        }
    }
}
