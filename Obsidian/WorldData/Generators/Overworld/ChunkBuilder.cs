using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld;

internal static class ChunkBuilder
{
    private static readonly Block bedrock = Registry.GetBlock(Material.Bedrock);
    private static readonly Block stone = Registry.GetBlock(Material.Stone);
    private static readonly Block caveAir = Registry.GetBlock(Material.CaveAir);

    internal static void FillChunk(Chunk chunk)
    {
        var terrainHeightmap = chunk.Heightmaps[HeightmapType.MotionBlocking];
        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                for (int by = -32; by < terrainHeightmap.GetHeight(bx, bz); by++)
                {
                    if (by <= -30 && by > 0) //TODO: better looking bedrock
                    {
                        chunk.SetBlock(bx, by, bz, bedrock);
                    }
                    else if (by <= terrainHeightmap.GetHeight(bx, bz))
                    {
                        chunk.SetBlock(bx, by, bz, stone);
                    }
                }
            }
        }
    }

    internal static void CarveCaves(GenHelper util, Chunk chunk)
    {
        Block block = caveAir;
        int chunkOffsetX = chunk.X * 16;
        int chunkOffsetZ = chunk.Z * 16;
        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                int terrainY = chunk.Heightmaps[HeightmapType.MotionBlocking].GetHeight(bx, bz);
                int brY = -30;
                for (int by = brY; by < terrainY; by++)
                {
                    bool isCave = util.Noise.Cave.GetValue(bx + chunkOffsetX, by, bz + chunkOffsetZ) > 0;
                    if (isCave)
                    {
                        chunk.SetBlock(bx, by, bz, block);
                    }
                }
            }
        }
    }
}
