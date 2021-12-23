using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.Terrain;

namespace Obsidian.WorldData.Generators.Overworld;

public static class ChunkBuilder
{
    public static void FillChunk(Chunk chunk, double[,] terrainHeightmap, double[,] bedrockHeightmap)
    {
        var bedrock = Registry.GetBlock(Material.Bedrock);
        var stone = Registry.GetBlock(Material.Stone);

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

    public static void CarveCaves(OverworldTerrain noiseGen, Chunk chunk, double[,] rhm, double[,] bhm, bool debug = false)
    {
        var b = Registry.GetBlock(Material.CaveAir);
        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                int tY = Math.Min((int)rhm[bx, bz], 64);
                int brY = (int)bhm[bx, bz];
                for (int by = brY; by < tY; by++)
                {
                    bool caveAir = noiseGen.IsCave(bx + (chunk.X * 16), by, bz + (chunk.Z * 16));
                    if (caveAir)
                    {
                        if (debug) { b = Registry.GetBlock(Material.LightGrayStainedGlass); }
                        chunk.SetBlock(bx, by, bz, b);
                    }
                }
            }
        }
    }
}
