using Obsidian.API;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Features;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Replays complete 26.3 features status regions over freshly generated biomes and terrain.</summary>
[Trait("Category", TestCategory.Worldgen)]
public class VanillaBiomeDecoration
{
    [Fact]
    public void BiomesTerrainAndDecorationMatchVanilla()
    {
        var cases = VanillaWorldgenCaptures.CurrentCases("decoration");

        WorldgenPipelines.AssertAllInSlices(cases, Name, AssertDecoration);
    }

    private static void AssertDecoration(JsonElement entry, WorldgenPipelines pipelines)
    {
        var dimension = entry.GetProperty("dimension").GetString()!;
        var pipeline = pipelines.Decoration(dimension, entry.GetProperty("seed").GetInt64());
        var chunkX = entry.GetProperty("chunkX").GetInt32();
        var chunkZ = entry.GetProperty("chunkZ").GetInt32();
        var minY = entry.GetProperty("minY").GetInt32();
        var height = entry.GetProperty("height").GetInt32();

        var chunks = Terrain(pipeline, chunkX, chunkZ, minY, height);

        AssertBiomes(entry, pipeline, chunks.Values);
        AssertFeatureOrder(entry, pipeline);

        var region = pipeline.Decorate(chunks, chunkX, chunkZ);

        Assert.Equal("minecraft:features", entry.GetProperty("status").GetString());
        Assert.Equal(ChunkGenStage.features, chunks[(chunkX, chunkZ)].ChunkStatus);

        foreach (var expected in entry.GetProperty("chunks").EnumerateArray())
        {
            var x = expected.GetProperty("chunkX").GetInt32();
            var z = expected.GetProperty("chunkZ").GetInt32();
            var chunk = (Chunk)chunks[(x, z)];

            VanillaTerrainGeneration.AssertBlocks(expected.GetProperty("blocks"), chunk);
            AssertHeightmaps(expected.GetProperty("heightmaps"), chunk, region);
        }

        var marks = entry.GetProperty("marks").EnumerateArray()
            .Select(row => new Vector(row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32()))
            .ToArray();
        var entities = entry.GetProperty("blockEntities").EnumerateArray()
            .Select(VanillaFeaturePlacement.ExpectedBlockEntity)
            .ToArray();

        Assert.Equal(marks, VanillaFeaturePlacement.FeatureMarks(chunks, chunkX, chunkZ, minY));
        Assert.Equal(entities, VanillaFeaturePlacement.BlockEntities(chunks, chunkX, chunkZ));
    }

    private static string Name(JsonElement entry) => entry.GetProperty("name").GetString()!;

    // The terrain-status chunks decoration of the center chunk reads and writes.
    private static Dictionary<(int X, int Z), IChunk> Terrain(
        BiomeTerrain pipeline, int chunkX, int chunkZ, int minY, int height)
    {
        var chunks = new Dictionary<(int X, int Z), IChunk>();

        for (var z = chunkZ - 1; z <= chunkZ + 1; z++)
        {
            for (var x = chunkX - 1; x <= chunkX + 1; x++)
            {
                var chunk = new Chunk(x, z, minY, height);
                pipeline.Generate(chunk);
                chunks.Add((x, z), chunk);
            }
        }

        return chunks;
    }

    private static void AssertBiomes(JsonElement entry, BiomeTerrain pipeline, IEnumerable<IChunk> chunks)
    {
        var present = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var chunk in chunks)
        {
            for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y += 4)
            {
                for (var localZ = 0; localZ < 16; localZ += 4)
                {
                    for (var localX = 0; localX < 16; localX += 4)
                        present.Add(chunk.GetBiome(localX, y, localZ).Name);
                }
            }
        }

        Assert.Equal(Strings(entry.GetProperty("possibleBiomes")), pipeline.Source.PossibleBiomes);
        Assert.Equal(Strings(entry.GetProperty("biomes")), present);

        if (entry.TryGetProperty("targetBiome", out var target))
            Assert.Contains(target.GetString(), present);
    }

    private static void AssertFeatureOrder(JsonElement entry, BiomeTerrain pipeline)
    {
        var order = entry.GetProperty("featureOrder").EnumerateArray().Select(Strings).ToArray();

        Assert.Equal(order.Length, pipeline.Decoration.Steps.Count);

        for (var step = 0; step < order.Length; step++)
            Assert.Equal(order[step], pipeline.Decoration.Steps[step]);
    }

    // The world generation heightmaps are the chunk's own; the final ones are those decoration tracked.
    private static void AssertHeightmaps(JsonElement expected, Chunk chunk, WorldGenRegion region)
    {
        foreach (var field in expected.EnumerateObject())
        {
            var type = Heightmap(field.Name);
            var expectedHeights = VanillaTerrainGeneration.Decode(field.Value.GetString()!);
            var actualHeights = new List<int>();

            for (var localZ = 0; localZ < 16; localZ++)
            {
                for (var localX = 0; localX < 16; localX++)
                {
                    var value = type is HeightmapType.WorldSurfaceWG or HeightmapType.OceanFloorWG
                        ? chunk.Heightmaps[type].GetHeight(localX, localZ)
                        : region.GetHeight(type, (chunk.X << 4) + localX, (chunk.Z << 4) + localZ);
                    actualHeights.Add(value);
                }
            }

            Assert.True(expectedHeights.SequenceEqual(actualHeights),
                $"{field.Name} differs in chunk ({chunk.X}, {chunk.Z}).");
        }
    }

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static HeightmapType Heightmap(string name) => name switch
    {
        "WORLD_SURFACE_WG" => HeightmapType.WorldSurfaceWG,
        "OCEAN_FLOOR_WG" => HeightmapType.OceanFloorWG,
        "WORLD_SURFACE" => HeightmapType.WorldSurface,
        "OCEAN_FLOOR" => HeightmapType.OceanFloor,
        "MOTION_BLOCKING" => HeightmapType.MotionBlocking,
        "MOTION_BLOCKING_NO_LEAVES" => HeightmapType.MotionBlockingNoLeaves,
        _ => throw new InvalidOperationException($"Unexpected heightmap {name}.")
    };
}
