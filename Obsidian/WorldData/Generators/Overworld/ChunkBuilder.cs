using Obsidian.ChunkData;
using Obsidian.Registries;

namespace Obsidian.WorldData.Generators.Overworld;

internal static class ChunkBuilder
{
    private const float CaveSize = 0.35F;
    private const float OreSize = 0.4F;
    private const float StoneAltSize = 1F;

    private static readonly IBlock[] stoneAlts = new IBlock[5]
    {
        BlocksRegistry.Andesite,
        BlocksRegistry.Diorite,
        BlocksRegistry.Granite,
        BlocksRegistry.Gravel,
        BlocksRegistry.Dirt
    };

    private static readonly IBlock[] ores = new IBlock[8]
    {
        BlocksRegistry.CoalOre,
        BlocksRegistry.IronOre,
        BlocksRegistry.CopperOre,
        BlocksRegistry.GoldOre,
        BlocksRegistry.LapisOre,
        BlocksRegistry.RedstoneOre,
        BlocksRegistry.EmeraldOre,
        BlocksRegistry.DiamondOre,
    };

    internal static void CavesAndOres(GenHelper helper, Chunk chunk)
    {
        int chunkOffsetX = chunk.X * 16;
        int chunkOffsetZ = chunk.Z * 16;
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int terrainY = chunk.Heightmaps[HeightmapType.MotionBlocking].GetHeight(x, z);
                var (worldX, worldZ) = (x + chunkOffsetX, z + chunkOffsetZ);
                for (int y = -60; y <= terrainY + 10; y++)
                {
                    bool isCave = helper.Noise.Cave.GetValue(x + chunkOffsetX, y, z + chunkOffsetZ) > 1 - CaveSize;
                    if (isCave)
                    {
                        if (chunk.GetBlock(x, y + 1, z) is IBlock b && !b.IsLiquid)
                            chunk.SetBlock(x, y, z, BlocksRegistry.CaveAir);
                        continue;
                    }
                    if (y > terrainY - 5) { continue; }
                    var orePlaced = false;
                    for (int i = 0; i < ores.Length; i++)
                    {
                        var oreNoise1 = helper.Noise.Ore(i).GetValue(worldX, y, worldZ);
                        var oreNoise2 = helper.Noise.Ore(i + ores.Length).GetValue(worldX, y, worldZ);
                        if (oreNoise1 > 1.0 - OreSize && oreNoise2 > 1.0 - OreSize)
                        {
                            chunk.SetBlock(worldX, y, worldZ, ores[i]);
                            orePlaced = true;
                            break;
                        }
                    }
                    if (orePlaced) { continue; }

                    for (int i = 0; i < stoneAlts.Length; i++)
                    {
                        var stoneNoise1 = helper.Noise.Stone(i).GetValue(worldX, y, worldZ);
                        var stoneNoise2 = helper.Noise.Stone(i + stoneAlts.Length).GetValue(worldX, y, worldZ);
                        if (stoneNoise1 > 1.5 - StoneAltSize && stoneNoise2 > 1.5 - StoneAltSize)
                        {
                            chunk.SetBlock(worldX, y, worldZ, stoneAlts[i]);
                            break;
                        }
                    }
                }
            }
        }
    }
}
