using Obsidian.API;
using Obsidian.WorldData.Generators.Overworld.Terrain;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class OverworldDecorator
    {
        public static void Decorate(Chunk chunk, double[,] terrainHeightMap, OverworldTerrain noise)
        {
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    var b = ChunkBiome.GetBiome((chunk.X << 4) + x, (chunk.Z << 4) + z, noise);
                    var blockPos = new Vector(x, (int)terrainHeightMap[x, z], z);
                    IDecorator decorator = DecoratorFactory.GetDecorator(b, chunk, blockPos, new BiomeNoise.TerrainNoise(noise.settings));
                    decorator.Decorate();
                }
            }
        }
    }
}
