using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Terrain.Carvers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Terrain;

/// <summary>26.3's terrain-local mask generation and fluid-aware mask application.</summary>
internal static class TerrainCarvers
{
    // What every chunk of a generator reads alike, read once per generator.
    private static readonly ConditionalWeakTable<TerrainGenerator, CarverData> data = new();

    public static void Apply(TerrainContext terrain, Func<int, int, IReadOnlyList<string>> carversAt)
    {
        var context = GenerateMask(terrain, carversAt);
        ApplyMask(terrain, context.Mask);
    }

    public static CarvingContext GenerateMask(TerrainContext terrain, Func<int, int, IReadOnlyList<string>> carversAt)
    {
        var context = new CarvingContext(terrain);
        var shared = data.GetValue(terrain.Generator, static generator => new CarverData(generator));
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
                    var configuration = shared.Configuration(sources[index]);

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
        var uncarvable = data.GetValue(terrain.Generator, static generator => new CarverData(generator)).Uncarvable;

        // Each column's carved runs, from the bottom up.
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                var bottom = mask.NextCarved(x, z, mask.MinY);

                while (bottom <= mask.MaxY)
                {
                    var top = mask.NextUncarved(x, z, bottom) - 1;
                    ApplySegment(terrain, uncarvable, x, z, bottom, top);
                    bottom = mask.NextCarved(x, z, top + 1);
                }
            }
        }
    }

    private static void ApplySegment(
        TerrainContext terrain,
        FrozenSet<int> uncarvable,
        int x,
        int z,
        int minY,
        int maxY)
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

                // Like the material column, vanilla marks any state with a fluid here.
                if (top.HasFluid())
                {
                    terrain.MarkForPostProcessing(blockX, y - 1, blockZ);
                }
            }
        }
    }

    /// <summary>A generator's carver configurations and the blocks carvers leave alone.</summary>
    private sealed class CarverData(TerrainGenerator generator)
    {
        private readonly ConcurrentDictionary<string, CarverConfiguration> configurations = new();

        public FrozenSet<int> Uncarvable { get; } = ReadUncarvable(generator.Data);

        public CarverConfiguration Configuration(string id) =>
            this.configurations.GetOrAdd(id, Read, generator);

        private static CarverConfiguration Read(string id, TerrainGenerator generator) =>
            CarverConfiguration.Read(generator.Data.Read("carver", id));

        private static FrozenSet<int> ReadUncarvable(Density.WorldgenData data)
        {
            var blocks = new HashSet<int>();
            ReadBlockTag(data, "minecraft:uncarvable", blocks);

            return blocks.ToFrozenSet();
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
