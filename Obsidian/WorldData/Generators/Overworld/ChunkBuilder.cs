using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Registries;

namespace Obsidian.WorldData.Generators.Overworld;

internal static class ChunkBuilder
{
    private const float caveSize = 0.45F;
    internal static void AddOresAndStoneAlts(GenHelper helper, Chunk chunk)
    {
        int chunkOffsetX = chunk.X * 16;
        int chunkOffsetZ = chunk.Z * 16;
        var stoneAlts = new IBlock[5]
        {
            BlocksRegistry.Andesite,
            BlocksRegistry.Diorite,
            BlocksRegistry.Granite,
            BlocksRegistry.Gravel,
            BlocksRegistry.Dirt
        };

        var ores = new IBlock[8]
        {
            BlocksRegistry.DiamondOre,
            BlocksRegistry.RedstoneOre,
            BlocksRegistry.LapisOre,
            BlocksRegistry.CopperOre,
            BlocksRegistry.GoldOre,
            BlocksRegistry.EmeraldOre,
            BlocksRegistry.IronOre,
            BlocksRegistry.CoalOre
        };

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int terrainY = chunk.Heightmaps[HeightmapType.MotionBlocking].GetHeight(x, z);
                var (worldX, worldZ) = (x + chunkOffsetX, z + chunkOffsetZ);

                for (int y = -60; y < terrainY; y++)
                {
                    for (int i = 0; i < stoneAlts.Length; i++)
                    {
                        var stoneNoise1 = helper.Noise.Stone(i).GetValue(worldX, y, worldZ);
                        var stoneNoise2 = helper.Noise.Stone(i + stoneAlts.Length).GetValue(worldX, y, worldZ);
                        if (stoneNoise1 > 0.5 && stoneNoise2 > 0.5)
                        {
                            chunk.SetBlock(worldX, y, worldZ, stoneAlts[i]);
                        }
                    }

                    for (int i = 0; i < ores.Length; i++)
                    {
                        var oreNoise1 = helper.Noise.Ore(i).GetValue(worldX, y, worldZ);
                        var oreNoise2 = helper.Noise.Ore(i + ores.Length).GetValue(worldX, y, worldZ);
                        
                        if (oreNoise1 > 1.0 && oreNoise2 > 1.0)
                        {
                            chunk.SetBlock(worldX, y, worldZ, ores[i]);
                        }
                    }
                }
            }
        }
    }

    internal static void CarveCaves(GenHelper util, Chunk chunk)
    {
        int chunkOffsetX = chunk.X * 16;
        int chunkOffsetZ = chunk.Z * 16;
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int terrainY = chunk.Heightmaps[HeightmapType.MotionBlocking].GetHeight(x, z);
                for (int by = -60; by <= terrainY + 5; by++)
                {
                    bool isCave = util.Noise.Cave.GetValue(x + chunkOffsetX, by, z + chunkOffsetZ) > 1 - caveSize;
                    if (isCave && chunk.GetBlock(x, by+1, z) is IBlock b && !b.IsLiquid)
                        chunk.SetBlock(x, by, z, BlocksRegistry.CaveAir);
                }
            }
        }
    }
}
