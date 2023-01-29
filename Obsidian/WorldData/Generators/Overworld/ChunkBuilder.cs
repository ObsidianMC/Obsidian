using Obsidian.ChunkData;
using Obsidian.Registries;

namespace Obsidian.WorldData.Generators.Overworld;

internal static class ChunkBuilder
{
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
                        chunk.SetBlock(bx, by, bz, BlocksRegistry.Bedrock);
                    }
                    else if (by <= terrainHeightmap.GetHeight(bx, bz))
                    {
                        chunk.SetBlock(bx, by, bz, BlocksRegistry.Stone);
                    }
                }
            }
        }
    }

    internal static void CarveCaves(GenHelper util, Chunk chunk)
    {
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
                        chunk.SetBlock(bx, by, bz, BlocksRegistry.CaveAir);
                    }
                }
            }
        }
    }
}
