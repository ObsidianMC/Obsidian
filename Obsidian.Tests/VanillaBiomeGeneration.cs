using Obsidian.API;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using TerrainGenerator = Obsidian.WorldData.Generators.Terrain.TerrainGenerator;

namespace Obsidian.Tests;

[Trait("Category", TestCategory.Worldgen)]
[Collection(WorldgenCollection.Name)]
public sealed class VanillaBiomeGeneration
{
    public static TheoryData<VanillaTerrainGeneration.TerrainCase> BiomeCases()
    {
        var cases = new TheoryData<VanillaTerrainGeneration.TerrainCase>();

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases("biomes"))
        {
            cases.Add(new VanillaTerrainGeneration.TerrainCase(entry));
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(BiomeCases))]
    public void BiomeSectionsMatchVanilla(VanillaTerrainGeneration.TerrainCase testCase)
    {
        var entry = testCase.Entry;
        var pipeline = Create(entry);
        var chunk = CreateChunk(entry);

        pipeline.Source.FillBiomes(chunk);

        Assert.Equal("minecraft:biomes", entry.GetProperty("status").GetString());
        Assert.Equal(ChunkGenStage.biomes, chunk.ChunkStatus);
        var palette = entry.GetProperty("palette").EnumerateArray().Select(value => value.GetString()!).ToArray();
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

    [Theory]
    [MemberData(nameof(VanillaTerrainGeneration.TerrainCases), MemberType = typeof(VanillaTerrainGeneration))]
    public void SelectedMaterialBiomesMatchVanilla(VanillaTerrainGeneration.TerrainCase testCase)
    {
        var entry = testCase.Entry;
        var pipeline = Create(entry);
        var chunk = CreateChunk(entry);
        var captured = entry.GetProperty("materialBiomes");
        var palette = captured.GetProperty("palette").EnumerateArray().Select(value => value.GetString()!).ToArray();
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

    [Theory]
    [MemberData(nameof(BiomeCases))]
    public void SpawnPositionsUseFullResolutionBiomes(VanillaTerrainGeneration.TerrainCase testCase)
    {
        var entry = testCase.Entry;
        var pipeline = Create(entry);
        var chunk = CreateChunk(entry);
        var zoomSeed = BiomeManager.ObfuscateSeed(entry.GetProperty("seed").GetInt64());
        var captured = entry.GetProperty("spawnBiomes");
        var palette = captured.GetProperty("palette").EnumerateArray().Select(value => value.GetString()!).ToArray();
        var expected = VanillaBiomes.Decode(captured.GetProperty("data").GetString()!);
        var chunks = new Dictionary<(int X, int Z), Chunk>();

        pipeline.Source.FillBiomes(chunk);
        chunks[(chunk.X, chunk.Z)] = chunk;

        // Include chunk and vertical storage boundaries. This reference reads stored sections independently of
        // materialBiomes, whose uncached point climate path can round differently at cave borders.
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

    [Theory]
    [MemberData(nameof(VanillaTerrainGeneration.TerrainCases), MemberType = typeof(VanillaTerrainGeneration))]
    public void BiomesAndTerrainMatchVanilla(VanillaTerrainGeneration.TerrainCase testCase)
    {
        var entry = testCase.Entry;
        var pipeline = Create(entry);
        var chunk = CreateChunk(entry);
        var updates = new List<Vector>();

        pipeline.Generate(chunk, updates);

        Assert.Equal(ChunkGenStage.carvers, chunk.ChunkStatus);
        VanillaTerrainGeneration.AssertBlocks(entry.GetProperty("blocks"), chunk);
        var expectedUpdates = entry.GetProperty("postProcessing").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        var actualUpdates = updates.OrderBy(position => (position.Y - chunk.MinY) >> 4)
            .Select(position => (position.Y - chunk.MinY) * 256 + (position.Z & 15) * 16 + (position.X & 15))
            .ToArray();
        Assert.Equal(expectedUpdates, actualUpdates);
    }

    private static Chunk CreateChunk(JsonElement entry) => new(
        entry.GetProperty("chunkX").GetInt32(),
        entry.GetProperty("chunkZ").GetInt32(),
        entry.GetProperty("minY").GetInt32(),
        entry.GetProperty("height").GetInt32());

    private static BiomeTerrain Create(JsonElement entry)
    {
        var dimension = entry.GetProperty("dimension").GetString()!;
        var settings = dimension switch
        {
            "minecraft:the_nether" => "minecraft:nether",
            "minecraft:the_end" => "minecraft:end",
            _ => dimension
        };
        var seed = entry.GetProperty("seed").GetInt64();
        var generator = new TerrainGenerator(WorldgenData.Vanilla, seed, settings);
        var preset = dimension == "minecraft:the_nether" ? "minecraft:nether" : dimension;

        return new BiomeTerrain(generator, preset);
    }
}
