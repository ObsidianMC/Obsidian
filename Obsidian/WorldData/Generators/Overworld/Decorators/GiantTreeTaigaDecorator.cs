using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class GiantTreeTaigaDecorator : BaseDecorator
    {
        public GiantTreeTaigaDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
            Features.Trees.Add(new DecoratorFeatures.TreeInfo(1, typeof(SpruceTree)));
            Features.Trees.Add(new DecoratorFeatures.TreeInfo(4, typeof(LargeSpruceTree)));
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
            var podzol = new Block(Material.Podzol, 1);

            chunk.SetBlock(pos, grassblock);
            for (int y = -1; y > -4; y--)
                chunk.SetBlock(pos + (0, y, 0), dirt);

            if (!chunk.GetBlock(pos + (0, 1, 0)).IsAir) { return; }

            var grass = Registry.GetBlock(Material.Grass);
            var grassNoise = noise.Decoration(worldX * 0.1, 0, worldZ * 0.1);
            if (grassNoise > 0 && grassNoise < 0.1) 
                chunk.SetBlock(pos + (0, 1, 0), grass);

            var dandelion = Registry.GetBlock(Material.Dandelion);
            var dandelionNoise = noise.Decoration(worldX * 0.1, 1, worldZ * 0.1);
            if (dandelionNoise > 0 && dandelionNoise < 0.05) 
            {
                chunk.SetBlock(pos + (0, 1, 0), dandelion);
            }

            var coarseDirt = new Block(Material.CoarseDirt, 0);
            if (noise.Decoration(worldX * 0.003, 10, worldZ * 0.003) > 0.5)
            {
                chunk.SetBlock(pos, coarseDirt);
            }
            
            if (noise.Decoration(worldX * 0.003, 18, worldZ * 0.003) > 0.5)
            {
                chunk.SetBlock(pos, podzol);
            }

            var berries = new Block(Material.SweetBerryBush, 2);
            if (noise.Decoration(worldX * 0.75, 4, worldZ * 0.75) > 0.95)
            {
                chunk.SetBlock(pos + (0, 1, 0), berries);
            }
        }
    }
}
