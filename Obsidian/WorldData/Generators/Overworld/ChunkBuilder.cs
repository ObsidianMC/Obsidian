using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Registries;

namespace Obsidian.WorldData.Generators.Overworld;

internal static class ChunkBuilder
{
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
                var (bx, bz) = (x + chunkOffsetX, z + chunkOffsetZ);

                for (int y = -30; y < terrainY; y++)
                {
                    for (int i = 0; i < stoneAlts.Length; i++)
                    {
                        var stoneNoise1 = helper.Noise.Stone(i).GetValue(bx, y, bz);
                        var stoneNoise2 = helper.Noise.Stone(i + stoneAlts.Length).GetValue(bx, y, bz);
                        if (stoneNoise1 > 0.5 && stoneNoise2 > 0.5)
                        {
                            if (!chunk.GetBlock(bx, y, bz).IsAir)
                            {
                                chunk.SetBlock(bx, y, bz, stoneAlts[i]);
                            }
                        }
                    }

                    for (int i = 0; i < ores.Length; i++)
                    {
                        var oreNoise1 = helper.Noise.Ore(i).GetValue(bx, y, bz);
                        var oreNoise2 = helper.Noise.Ore(i + ores.Length).GetValue(bx, y, bz);
                        
                        if (oreNoise1 > 1.0 && oreNoise2 > 1.0)
                        {
                            if (TagsRegistry.Blocks.StoneOreReplaceables.Entries.Contains(chunk.GetBlock(bx, y, bz)?.DefaultId ?? 0))
                            {
                                chunk.SetBlock(bx, y, bz, ores[i]);
                            }
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
        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                int terrainY = chunk.Heightmaps[HeightmapType.MotionBlocking].GetHeight(bx, bz);
                int brY = -60;
                for (int by = brY; by <= terrainY + 2; by++)
                {
                    bool isCave = util.Noise.Cave.GetValue(bx + chunkOffsetX, by, bz + chunkOffsetZ) > 0.55;
                    if (isCave)
                    {
                        chunk.SetBlock(bx, by, bz, BlocksRegistry.CaveAir);
                    }
                }
            }
        }
    }
}
