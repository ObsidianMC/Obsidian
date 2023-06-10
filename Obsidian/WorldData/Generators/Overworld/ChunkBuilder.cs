using Obsidian.ChunkData;
using Obsidian.Registries;
using System.Linq;

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

    internal static void Biomes(GenHelper helper, Chunk chunk)
    {
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int worldX = x + (chunk.X << 4);
                int worldZ = z + (chunk.Z << 4);

                // Determine Biome
                if (x % 4 == 0 && z % 4 == 0) // Biomes are in 4x4x4 blocks. Do a 2D array for now and just copy it vertically.
                {
                    var biome = (Biome)helper.Noise.Biome.GetValue(worldX, 0, worldZ);
                    for (int y = -64; y < 320; y += 4)
                    {
                        chunk.SetBiome(x, y, z, biome);
                    }
                }
            }
        }
    }

    internal static void Surface(GenHelper helper, Chunk chunk)
    {
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int worldX = x + (chunk.X << 4);
                int worldZ = z + (chunk.Z << 4);
                int terrainHeight = -64;
                // Search for the surface. Start at sea level...
                // If stone, scan upwards until 64 consecutive air
                // If air, scan downwards until 64 consecutive stone
                if (helper.Noise.IsTerrain(worldX, 64, worldZ))
                {
                    int airCount = 0;
                    for (int y = 64; y < 320; y++)
                    {
                        if (helper.Noise.IsTerrain(worldX, y, worldZ))
                        {
                            terrainHeight = y;
                            chunk.SetBlock(x, y, z, BlocksRegistry.Stone);
                        }
                        else
                        {
                            airCount++;
                        }
                        if (airCount == 30)
                        {
                            break;
                        }
                    }
                    for (int y = 64; y >= -64; y--)
                    {
                        chunk.SetBlock(x, y, z, BlocksRegistry.Stone);
                    }
                }
                else
                {
                    int solidCount = 0;
                    for (int y = 64; y >= -64; y--)
                    {
                        if (solidCount <= 30)
                        {
                            if (helper.Noise.IsTerrain(worldX, y, worldZ))
                            {
                                solidCount++;
                                chunk.SetBlock(x, y, z, BlocksRegistry.Stone);
                                terrainHeight = Math.Max(terrainHeight, y);
                            }
                            else
                            {
                                chunk.SetBlock(x, y, z, BlocksRegistry.Water);
                            }
                        }
                        else
                        {
                            chunk.SetBlock(x, y, z, BlocksRegistry.Stone);
                        }
                    }
                }

                chunk.Heightmaps[HeightmapType.WorldSurfaceWG].Set(x, z, terrainHeight);
            }
        }
    }

    internal static void CavesAndOres(GenHelper helper, Chunk chunk)
    {
        int chunkOffsetX = chunk.X * 16;
        int chunkOffsetZ = chunk.Z * 16;
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int terrainY = chunk.Heightmaps[HeightmapType.WorldSurfaceWG].GetHeight(x, z);
                var (worldX, worldZ) = (x + chunkOffsetX, z + chunkOffsetZ);
                for (int y = -60; y <= terrainY; y++)
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

    internal static void UpdateWGHeightmap(Chunk chunk)
    {
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int oldY = chunk.Heightmaps[HeightmapType.WorldSurfaceWG].GetHeight(x, z);
                for (int y = oldY; y >= -64; y--)
                {
                    var b = chunk.GetBlock(x, y, z);
                    if (!b.IsAir)
                    {
                        chunk.Heightmaps[HeightmapType.WorldSurfaceWG].Set(x, z, y);
                        break;
                    }
                }
            }
        }
    }

    internal static void Heightmaps(Chunk chunk)
    {
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                bool worldSurfaceSet = false;
                bool motionBlockingSet = false;
                bool motionBlockingLeavesSet = false;
                chunk.Heightmaps[HeightmapType.OceanFloor] = chunk.Heightmaps[HeightmapType.WorldSurfaceWG];

                for (int y = 319; y >= -64; y--)
                {
                    var secIndex = (y >> 4) + 4;
                    if (chunk.Sections[secIndex].IsEmpty)
                    {
                        y -= 15;
                        continue;
                    }

                    var b = chunk.GetBlock(x, y, z);
                    if (!worldSurfaceSet && !b.IsAir)
                    {
                        chunk.Heightmaps[HeightmapType.WorldSurface].Set(x, z, y);
                        worldSurfaceSet = true;
                    }

                    if (!motionBlockingSet &&
                        !TagsRegistry.Blocks.ReplaceablePlants.Entries.Contains(b.RegistryId) &&
                        !TagsRegistry.Blocks.Saplings.Entries.Contains(b.RegistryId) &&
                        !TagsRegistry.Blocks.Crops.Entries.Contains(b.RegistryId) &&
                        !TagsRegistry.Blocks.Flowers.Entries.Contains(b.RegistryId) &&
                        !TagsRegistry.Fluids.Water.Entries.Contains(b.RegistryId)
                        )
                    {
                        chunk.Heightmaps[HeightmapType.MotionBlocking].Set(x, z, y);
                        motionBlockingSet = true;
                    }

                    if (!motionBlockingLeavesSet &&
                        !TagsRegistry.Blocks.ReplaceablePlants.Entries.Contains(b.RegistryId) &&
                        !TagsRegistry.Blocks.Saplings.Entries.Contains(b.RegistryId) &&
                        !TagsRegistry.Blocks.Crops.Entries.Contains(b.RegistryId) &&
                        !TagsRegistry.Blocks.Flowers.Entries.Contains(b.RegistryId) &&
                        !TagsRegistry.Fluids.Water.Entries.Contains(b.RegistryId) &&
                        !TagsRegistry.Blocks.Leaves.Entries.Contains(b.RegistryId)
                        )
                    {
                        chunk.Heightmaps[HeightmapType.MotionBlockingNoLeaves].Set(x, z, y);
                        motionBlockingLeavesSet = true;
                    }

                    if (worldSurfaceSet && motionBlockingSet && motionBlockingLeavesSet)
                    {
                        break;
                    }
                }
            }
        }
    }
}
