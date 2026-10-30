using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Terrain.Carvers;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Terrain;

/// <summary>26.3's terrain-local mask generation and fluid-aware mask application.</summary>
internal static class TerrainCarvers
{
    public static void Apply(TerrainContext terrain, Func<int, int, IReadOnlyList<string>> carversAt)
    {
        var context = GenerateMask(terrain, carversAt);
        ApplyMask(terrain, context.Mask);
    }

    public static CarvingContext GenerateMask(TerrainContext terrain, Func<int, int, IReadOnlyList<string>> carversAt)
    {
        var context = new CarvingContext(terrain);
        var configurations = new Dictionary<string, CarverConfiguration>();
        var cave = new CaveWorldCarver();
        var canyon = new CanyonWorldCarver();
        var random = new WorldgenRandom(new LegacyRandomSource(0L));
        var chunk = terrain.Chunk;

        for (var sourceX = chunk.X - 8; sourceX <= chunk.X + 8; sourceX++)
        {
            for (var sourceZ = chunk.Z - 8; sourceZ <= chunk.Z + 8; sourceZ++)
            {
                var sources = carversAt(sourceX, sourceZ);

                for (var index = 0; index < sources.Count; index++)
                {
                    var id = sources[index];

                    if (!configurations.TryGetValue(id, out var configuration))
                    {
                        configuration = CarverConfiguration.Read(terrain.Generator.Data.Read("carver", id));
                        configurations.Add(id, configuration);
                    }

                    // Every carver starts a fresh large-feature sequence, offset by its biome-list index.
                    random.SetLargeFeatureSeed(unchecked(terrain.Generator.Seed + index), sourceX, sourceZ);

                    if (random.NextFloat() > configuration.Probability)
                    {
                        continue;
                    }

                    switch (configuration)
                    {
                        case CaveCarverConfiguration caves:
                            cave.Carve(context, caves, random, sourceX, sourceZ);
                            break;
                        case CanyonCarverConfiguration canyons:
                            canyon.Carve(context, canyons, random, sourceX, sourceZ);
                            break;
                    }
                }
            }
        }

        return context;
    }

    public static void ApplyMask(TerrainContext terrain, CarvingMask mask)
    {
        var uncarvable = new HashSet<int>();
        ReadBlockTag(terrain.Generator.Data, "minecraft:uncarvable", uncarvable);

        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                var y = mask.MinY;

                while (y <= mask.MaxY)
                {
                    if (!mask.Get(x, y, z))
                    {
                        y++;
                        continue;
                    }

                    var bottom = y;

                    while (y <= mask.MaxY && mask.Get(x, y, z))
                    {
                        y++;
                    }

                    ApplySegment(terrain, uncarvable, x, z, bottom, y - 1);
                }
            }
        }
    }

    private static void ApplySegment(TerrainContext terrain, HashSet<int> uncarvable, int x, int z, int minY, int maxY)
    {
        var chunk = terrain.Chunk;
        var blockX = terrain.Volume.BlockX(x);
        var blockZ = terrain.Volume.BlockZ(z);
        var surfaceReached = false;

        for (var y = maxY; y >= minY; y--)
        {
            var old = chunk.GetBlock(x, y, z);

            if (uncarvable.Contains(old.RegistryId))
            {
                continue;
            }

            if (old.RegistryId is BlockIds.GrassBlock or BlockIds.Mycelium)
            {
                surfaceReached = true;
            }

            var block = terrain.Aquifer.ComputeSubstance(blockX, y, blockZ, 0.0);

            if (block is null)
            {
                continue;
            }

            chunk.SetBlock(x, y, z, block);
            MaterialRules.UpdateHeight(chunk, HeightmapType.WorldSurfaceWG, x, y, z, block);
            MaterialRules.UpdateHeight(chunk, HeightmapType.OceanFloorWG, x, y, z, block);
            terrain.ScheduleFluid(blockX, y, blockZ, block);

            if (!surfaceReached || !chunk.GetBlock(x, y - 1, z).Is(BlockIds.Dirt))
            {
                continue;
            }

            var top = terrain.Generator.Materials.TopMaterial(terrain, blockX, y - 1, blockZ, block.IsLiquid);

            if (top is not null)
            {
                chunk.SetBlock(x, y - 1, z, top);

                if (top.IsLiquid)
                {
                    terrain.FluidUpdates?.Add(new Vector(blockX, y - 1, blockZ));
                }
            }
        }
    }

    private static void ReadBlockTag(Density.WorldgenData data, string id, HashSet<int> blocks)
    {
        foreach (var entry in data.Read("block_tag", id).GetProperty("values").EnumerateArray())
        {
            var name = entry.ValueKind == JsonValueKind.String
                ? entry.GetString()!
                : entry.GetProperty("id").GetString()!;

            if (name.StartsWith('#'))
            {
                ReadBlockTag(data, name[1..], blocks);
            }
            else
            {
                blocks.Add(BlocksRegistry.Get(name).RegistryId);
            }
        }
    }
}
