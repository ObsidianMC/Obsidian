using Obsidian.API;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class OverworldDecorator
    {
        public static void Decorate(Chunk chunk, double[,] terrainHeightMap, OverworldNoise noise)
        {
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    var b = ChunkBiome.GetBiome((chunk.X << 4) + x, (chunk.Z << 4) + z, noise);
                    IDecorator decorator = DecoratorFactory.GetDecorator(b);
                    var blockPos = new Position(x, (int)terrainHeightMap[x, z], z);
                    decorator.Decorate(chunk, blockPos, noise);
                }
            }
        }
    }
}
