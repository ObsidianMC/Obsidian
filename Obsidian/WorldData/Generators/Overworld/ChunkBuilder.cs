using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.Terrain;

namespace Obsidian.WorldData.Generators.Overworld;

public static class ChunkBuilder
{
    private static readonly Block bedrock = Registry.GetBlock(Material.Bedrock);
    private static readonly Block stone = Registry.GetBlock(Material.Stone);
    private static readonly Block caveAir = Registry.GetBlock(Material.CaveAir);

    public static void FillChunk(Chunk chunk, double[,] terrainHeightmap, double[,] bedrockHeightmap)
    {
        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                for (int by = -32; by < terrainHeightmap[bx, bz]; by++)
                {
                    if (by <= bedrockHeightmap[bx, bz] && by > 0)
                    {
                        chunk.SetBlock(bx, by, bz, bedrock);
                    }
                    else if (by <= terrainHeightmap[bx, bz])
                    {
                        chunk.SetBlock(bx, by, bz, stone);
                    }
                }
            }
        }
    }

    public static void CarveCaves(OverworldTerrain noiseGen, Chunk chunk, double[,] thm, double[,] bhm)
    {
        Block block = caveAir;
        int chunkOffsetX = chunk.X * 16;
        int chunkOffsetZ = chunk.Z * 16;
        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                int tY = (int)++thm[bx, bz];
                int brY = (int)bhm[bx, bz];
                for (int by = brY; by < tY; by++)
                {
                    bool isCave = noiseGen.IsCave(bx + chunkOffsetX, by, bz + chunkOffsetZ);
                    if (isCave)
                    {
                        chunk.SetBlock(bx, by, bz, block);
                    }
                }
            }
        }
    }
}
