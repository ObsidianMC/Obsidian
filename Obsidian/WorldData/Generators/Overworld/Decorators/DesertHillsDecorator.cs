﻿using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class DesertHillsDecorator : BaseDecorator
    {
        public DesertHillsDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
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
            
            var sand = Registry.GetBlock(Material.Sand);
            var sandstone = Registry.GetBlock(Material.Sandstone);
            var deadbush = Registry.GetBlock(Material.DeadBush);
            var cactus = Registry.GetBlock(Material.Cactus);

            for (int y = 0; y > -4; y--)
                chunk.SetBlock(pos + (0, y, 0), sand);
            for (int y = -4; y > -7; y--)
                chunk.SetBlock(pos + (0, y, 0), sandstone);

            var bushNoise = noise.Decoration(worldX * 0.1, 0, worldZ * 0.1);
            if (bushNoise > 0 && bushNoise < 0.05) // 5% chance for bush
                chunk.SetBlock(pos + (0, 1, 0), deadbush);

            var cactusNoise = noise.Decoration(worldX * 0.1, 1, worldZ * 0.1);
            if (cactusNoise > 0 && cactusNoise < 0.01) // 1% chance for cactus
            {
                chunk.SetBlock(pos + (0, 1, 0), cactus);
                chunk.SetBlock(pos + (0, 2, 0), cactus);
                chunk.SetBlock(pos + (0, 3, 0), cactus);
            }
        }
    }
}
