using Obsidian.API;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;
using Obsidian.WorldData.Generators.Overworld.Terrain;
using System;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class OverworldDecorator
    {
        public static void Decorate(Chunk chunk, double[,] terrainHeightMap, OverworldTerrain ot)
        {
            var noise = new BiomeNoise.TerrainNoise(ot.settings);
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    var b = ChunkBiome.GetBiome((chunk.X << 4) + x, (chunk.Z << 4) + z, ot);
                    var blockPos = new Vector(x, (int)terrainHeightMap[x, z], z);
                    IDecorator decorator = DecoratorFactory.GetDecorator(b, chunk, blockPos, noise);
                    decorator.Decorate();
                }
            }
        }
    }
}
