using Obsidian.API;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Mojang;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Replays the 26.3 <c>biomes</c> references through the biome source, and the <c>terrain</c> references through the
/// whole pipeline, with the biome source selecting the biomes and carvers <see cref="VanillaTerrainGeneration"/>
/// supplies.
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
public sealed class VanillaBiomeGeneration
{
    // A chunk's stored biome sections, and the biomes natural spawning reads from them.
    [Fact]
    public void BiomesMatchVanilla()
    {
        var pipelines = new WorldgenPipelines();
        var stages = VanillaWorldgenCaptures.CurrentCases("biomes").SelectMany(entry => BiomeStages(entry, pipelines));

        FixtureCases.AssertAll(stages, stage => stage.Name, stage => stage.Check());
    }

    // The block biomes material rules read, and the terrain status the biome source drives.
    [Fact]
    public void TerrainWithBiomeSourceMatchesVanilla()
    {
        var pipelines = new WorldgenPipelines();
        var stages = VanillaWorldgenCaptures.CurrentCases("terrain")
            .SelectMany(entry => TerrainStages(entry, pipelines));

        FixtureCases.AssertAll(stages, stage => stage.Name, stage => stage.Check());
    }

    private sealed record Stage(string Name, Action Check);

    private static IEnumerable<Stage> BiomeStages(JsonElement entry, WorldgenPipelines pipelines)
    {
        var name = VanillaTerrainGeneration.CaseName(entry);

        yield return new Stage($"{name} sections", () => AssertSections(entry, Pipeline(entry, pipelines)));
        yield return new Stage($"{name} spawn biomes", () => AssertSpawnBiomes(entry, Pipeline(entry, pipelines)));
    }

    private static IEnumerable<Stage> TerrainStages(JsonElement entry, WorldgenPipelines pipelines)
    {
        var name = VanillaTerrainGeneration.CaseName(entry);

        yield return new Stage(
            $"{name} material biomes",
            () => AssertMaterialBiomes(entry, Pipeline(entry, pipelines)));
        yield return new Stage($"{name} terrain", () => AssertTerrain(entry, Pipeline(entry, pipelines)));
    }

    private static void AssertSections(JsonElement entry, BiomeTerrain pipeline)
    {
        var chunk = CreateChunk(entry);

        pipeline.Source.FillBiomes(chunk);

        Assert.Equal("minecraft:biomes", entry.GetProperty("status").GetString());
        Assert.Equal(ChunkGenStage.biomes, chunk.ChunkStatus);

        var palette = Strings(entry.GetProperty("palette"));
        var expected = VanillaBiomes.Decode(entry.GetProperty("data").GetString()!);
        var index = 0;

        for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y += 4)
        {
            for (var z = 0; z < 16; z += 4)
            {
                for (var x = 0; x < 16; x += 4)
                {
                    Assert.True(palette[expected[index]] == chunk.GetBiome(x, y, z).Name,
                        $"Biome at quart ({x >> 2}, {y >> 2}, {z >> 2}), buffer index {index}");
                    index++;
                }
            }
        }

        Assert.Equal(expected.Length, index);
    }

    // Spawning reads stored sections, independently of materialBiomes, whose uncached point climate path can round
    // differently at cave borders. The positions include chunk and vertical storage boundaries.
    private static void AssertSpawnBiomes(JsonElement entry, BiomeTerrain pipeline)
    {
        var chunk = CreateChunk(entry);
        var zoomSeed = BiomeManager.ObfuscateSeed(entry.GetProperty("seed").GetInt64());
        var captured = entry.GetProperty("spawnBiomes");
        var palette = Strings(captured.GetProperty("palette"));
        var expected = VanillaBiomes.Decode(captured.GetProperty("data").GetString()!);
        var chunks = new Dictionary<(int X, int Z), Chunk>();

        pipeline.Source.FillBiomes(chunk);
        chunks[(chunk.X, chunk.Z)] = chunk;

        for (var z = 0; z < 16; z++)
        {
            for (var x = 0; x < 16; x++)
            {
                for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y += 4)
                {
                    var actual = MobSpawner.SpawnBiomeAt(zoomSeed, LoadedChunk, chunk,
                        chunk.X * 16 + x, y, chunk.Z * 16 + z);
                    var index = (z * 16 + x) * chunk.Height + y - chunk.MinY;
                    Assert.Equal(palette[expected[index]], actual.Name);
                }
            }
        }

        Chunk LoadedChunk(int x, int z)
        {
            if (!chunks.TryGetValue((x, z), out var neighbor))
            {
                neighbor = new Chunk(x, z, chunk.MinY, chunk.Height);
                pipeline.Source.FillBiomes(neighbor);
                chunks[(x, z)] = neighbor;
            }

            return neighbor;
        }
    }

    private static void AssertMaterialBiomes(JsonElement entry, BiomeTerrain pipeline)
    {
        var chunk = CreateChunk(entry);
        var captured = entry.GetProperty("materialBiomes");
        var palette = Strings(captured.GetProperty("palette"));
        var expected = VanillaBiomes.Decode(captured.GetProperty("data").GetString()!);

        pipeline.Source.FillBiomes(chunk);

        var index = 0;

        for (var z = 0; z < 16; z++)
        {
            for (var x = 0; x < 16; x++)
            {
                for (var y = chunk.MinY; y <= chunk.MinY + chunk.Height; y++)
                {
                    var actual = pipeline.BiomeAt(chunk.X * 16 + x, y, chunk.Z * 16 + z);
                    Assert.True(palette[expected[index]] == actual,
                        $"Material biome at ({x}, {y}, {z}): expected {palette[expected[index]]}, actual {actual}");
                    index++;
                }
            }
        }
    }

    private static void AssertTerrain(JsonElement entry, BiomeTerrain pipeline)
    {
        var chunk = CreateChunk(entry);
        var updates = new List<Vector>();

        pipeline.Generate(chunk, updates);

        var expectedUpdates = entry.GetProperty("postProcessing").EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();
        Assert.Equal(ChunkGenStage.carvers, chunk.ChunkStatus);
        VanillaTerrainGeneration.AssertBlocks(entry.GetProperty("blocks"), chunk);
        Assert.Equal(expectedUpdates, VanillaTerrainGeneration.PostProcessingIndices(updates, chunk));
    }

    private static BiomeTerrain Pipeline(JsonElement entry, WorldgenPipelines pipelines) => pipelines.Biomes(
        entry.GetProperty("dimension").GetString()!,
        entry.GetProperty("seed").GetInt64());

    private static Chunk CreateChunk(JsonElement entry) => new(
        entry.GetProperty("chunkX").GetInt32(),
        entry.GetProperty("chunkZ").GetInt32(),
        entry.GetProperty("minY").GetInt32(),
        entry.GetProperty("height").GetInt32());

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetString()!).ToArray();
}
