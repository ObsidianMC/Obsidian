using Obsidian.ChunkData;
using Obsidian.Registries;
using System.Linq;

namespace Obsidian.WorldData.Generators.Overworld;

internal static class ChunkBuilder
{
    private const float CaveSize = 0.35F;
    private const float OreSize = 0.4F;
    private const float StoneAltSize = 1F;

    private static readonly IBlock[] stoneAlts =
    [
        BlocksRegistry.Andesite,
        BlocksRegistry.Diorite,
        BlocksRegistry.Granite,
        BlocksRegistry.Gravel,
        BlocksRegistry.Dirt,
        BlocksRegistry.Tuff
    ];

    private static readonly IBlock[] deepstoneAlts =
    [
        BlocksRegistry.Gravel,
        BlocksRegistry.Tuff,
        BlocksRegistry.Gravel,
        BlocksRegistry.Tuff,
        BlocksRegistry.Gravel,
        BlocksRegistry.Tuff
    ];

    private static readonly IBlock[] ores =
    [
        BlocksRegistry.CoalOre,
        BlocksRegistry.IronOre,
        BlocksRegistry.CopperOre,
        BlocksRegistry.GoldOre,
        BlocksRegistry.LapisOre,
        BlocksRegistry.RedstoneOre,
        BlocksRegistry.EmeraldOre,
        BlocksRegistry.DiamondOre,
    ];

    private static readonly IBlock[] deepores =
    [
        BlocksRegistry.DeepslateCoalOre,
        BlocksRegistry.DeepslateIronOre,
        BlocksRegistry.DeepslateCopperOre,
        BlocksRegistry.DeepslateGoldOre,
        BlocksRegistry.DeepslateLapisOre,
        BlocksRegistry.DeepslateRedstoneOre,
        BlocksRegistry.DeepslateEmeraldOre,
        BlocksRegistry.DeepslateDiamondOre,
    ];

    internal enum OreType : int
    {
        Coal,
        Iron,
        Copper,
        Gold,
        Lapis,
        Redstone,
        Emerald,
        Diamond
    }
    
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
                // If stone, scan upwards until 30 consecutive air
                // If air, scan downwards until 30 consecutive stone
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
                    for (int y = 64; y > 0; y--)
                    {
                        chunk.SetBlock(x, y, z, BlocksRegistry.Stone);
                    }
                    for (int y = 0; y >= -63; y--)
                    {
                        chunk.SetBlock(x, y, z, BlocksRegistry.Deepslate);
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
                                if (y <= 0)
                                {
                                    chunk.SetBlock(x, y, z, BlocksRegistry.Deepslate);
                                }
                                else
                                {
                                    chunk.SetBlock(x, y, z, BlocksRegistry.Stone);
                                }
                                terrainHeight = Math.Max(terrainHeight, y);
                            }
                            else
                            {
                                chunk.SetBlock(x, y, z, BlocksRegistry.Water);
                            }
                        }
                        else
                        {
                            if (y <= 0)
                            {
                                chunk.SetBlock(x, y, z, BlocksRegistry.Deepslate);
                            }
                            else
                            {
                                chunk.SetBlock(x, y, z, BlocksRegistry.Stone);
                            }
                        }
                    }
                }

                chunk.SetBlock(x, -64, z, BlocksRegistry.Bedrock);
                chunk.Heightmaps[HeightmapType.WorldSurfaceWG].Set(x, z, terrainHeight);
            }
        }
    }

    internal static bool GenerateOreCheck(int height, OreType type) => type switch
    {
        OreType.Coal => height >= 0 && height <= 320,
        OreType.Iron => (height >= -63 && height <= 72) || (height >= 80 && height <= 320),
        OreType.Copper => height >= -16 && height <= 112,
        OreType.Gold => height >= -63 && height <= 30,
        OreType.Lapis => height >= -63 && height <= 64,
        OreType.Redstone => height >= -63 && height <= 16,
        OreType.Emerald => height >= -16 && height <= 320,
        OreType.Diamond => height >= -63 && height <= 16,
        _ => true
    };
    
    internal static void CavesAndOres(GenHelper helper, Chunk chunk)
    {
        int chunkOffsetX = chunk.X * 16;
        int chunkOffsetZ = chunk.Z * 16;
        List<Biome> emeraldBiomes = [Biome.WindsweptHills, Biome.WindsweptGravellyHills, Biome.Meadow, Biome.Grove, Biome.SnowySlopes, Biome.FrozenPeaks, Biome.JaggedPeaks, Biome.StonyPeaks];
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
                    var orePlaced = false; //Thanks Jonpro03 for line 210 to 237!
                    foreach (OreType i in Enum.GetValues(typeof(OreType)))
                    {
                        // Check if we should be placing a given ore at this Y level
                        if (!GenerateOreCheck(y, i))
                        {
                            // move on to the next ore
                            continue;
                        }
                    
                        var b = chunk.GetBiome(x, y, z);
                        // Check that Emerald is only placed in the right biomes
                        if (i == OreType.Emerald && !emeraldBiomes.Contains(b))
                        {
                            continue;
                        }
                    
                        var oreNoise1 = helper.Noise.Ore((int)i).GetValue(worldX, y, worldZ);
                        var oreNoise2 = helper.Noise.Ore((int)i + ores.Length).GetValue(worldX, y, worldZ);
                        if (oreNoise1 > 1.0 - OreSize && oreNoise2 > 1.0 - OreSize)
                        {
                            // If Y is below 0, switch to deepore varients
                            chunk.SetBlock(worldX, y, worldZ, y > 0 ? ores[(int)i] : deepores[(int)i]);
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
                            chunk.SetBlock(worldX, y, worldZ, y > 0 ? stoneAlts[i] : deepstoneAlts[i]);
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
                        !TagsRegistry.Blocks.ReplaceableByLiquid.Entries.Contains(b.RegistryId) &&
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
                        !TagsRegistry.Blocks.ReplaceableByLiquid.Entries.Contains(b.RegistryId) &&
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
