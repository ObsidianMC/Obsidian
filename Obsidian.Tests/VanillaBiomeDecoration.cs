using Obsidian.API;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang;
using TerrainGenerator = Obsidian.WorldData.Generators.Terrain.TerrainGenerator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>Replays complete 26.3 features status regions over freshly generated biomes and terrain.</summary>
[Trait("Category", TestCategory.Worldgen)]
[Collection(WorldgenCollection.Name)]
public class VanillaBiomeDecoration(ITestOutputHelper output)
{
    public static TheoryData<string> Cases()
    {
        var cases = new TheoryData<string>();

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases("decoration"))
        {
            cases.Add(entry.GetProperty("name").GetString()!);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BiomesTerrainAndDecorationMatchVanilla(string name)
    {
        var entry = VanillaWorldgenCaptures.CurrentCases("decoration")
            .Single(entry => entry.GetProperty("name").GetString() == name);
        var dimension = entry.GetProperty("dimension").GetString()!;
        var settings = dimension switch
        {
            "minecraft:the_nether" => "minecraft:nether",
            "minecraft:the_end" => "minecraft:end",
            _ => dimension
        };
        var preset = dimension == "minecraft:the_nether" ? "minecraft:nether" : dimension;
        var seed = entry.GetProperty("seed").GetInt64();
        var terrain = new TerrainGenerator(WorldgenData.Vanilla, seed, settings);
        var registry = new FeatureRegistry(WorldgenData.Vanilla) { Template = VanillaFeatureTemplates.Get };
        var pipeline = new BiomeTerrain(terrain, preset, registry);
        var chunkX = entry.GetProperty("chunkX").GetInt32();
        var chunkZ = entry.GetProperty("chunkZ").GetInt32();
        var minY = entry.GetProperty("minY").GetInt32();
        var height = entry.GetProperty("height").GetInt32();
        var chunks = new Dictionary<(int X, int Z), IChunk>();
        var present = new SortedSet<string>(StringComparer.Ordinal);

        for (var z = chunkZ - 1; z <= chunkZ + 1; z++)
        {
            for (var x = chunkX - 1; x <= chunkX + 1; x++)
            {
                var chunk = new Chunk(x, z, minY, height);
                pipeline.Generate(chunk);
                chunks.Add((x, z), chunk);

                for (var y = minY; y < minY + height; y += 4)
                {
                    for (var localZ = 0; localZ < 16; localZ += 4)
                    {
                        for (var localX = 0; localX < 16; localX += 4)
                        {
                            present.Add(chunk.GetBiome(localX, y, localZ).Name);
                        }
                    }
                }
            }
        }

        var possible = Strings(entry.GetProperty("possibleBiomes"));
        Assert.Equal(possible, pipeline.Source.PossibleBiomes);
        Assert.Equal(Strings(entry.GetProperty("biomes")), present);

        if (entry.TryGetProperty("targetBiome", out var target))
        {
            Assert.Contains(target.GetString(), present);
        }

        var order = entry.GetProperty("featureOrder").EnumerateArray().Select(Strings).ToArray();
        Assert.Equal(order.Length, pipeline.Decoration.Steps.Count);

        for (var step = 0; step < order.Length; step++)
        {
            Assert.Equal(order[step], pipeline.Decoration.Steps[step]);
        }

        output.WriteLine($"Terrain marks: {chunks.Values.Cast<Chunk>().Sum(chunk => chunk.PostProcessing.Count)}");
        var region = pipeline.Decorate(chunks, chunkX, chunkZ);
        output.WriteLine($"Decorated marks: {chunks.Values.Cast<Chunk>().Sum(chunk => chunk.PostProcessing.Count)}");

        Assert.Equal("minecraft:features", entry.GetProperty("status").GetString());
        Assert.Equal(ChunkGenStage.features, chunks[(chunkX, chunkZ)].ChunkStatus);

        foreach (var expected in entry.GetProperty("chunks").EnumerateArray())
        {
            var x = expected.GetProperty("chunkX").GetInt32();
            var z = expected.GetProperty("chunkZ").GetInt32();
            var chunk = (Chunk)chunks[(x, z)];
            VanillaTerrainGeneration.AssertBlocks(expected.GetProperty("blocks"), chunk);

            foreach (var field in expected.GetProperty("heightmaps").EnumerateObject())
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
                            : region.GetHeight(type, x * 16 + localX, z * 16 + localZ);
                        actualHeights.Add(value);
                    }
                }

                Assert.True(expectedHeights.SequenceEqual(actualHeights),
                    $"{field.Name} differs in chunk ({x}, {z}).");
            }
        }

        var marks = entry.GetProperty("marks").EnumerateArray()
            .Select(row => new Vector(row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32())).ToArray();
        Assert.Equal(marks, VanillaFeaturePlacement.FeatureMarks(chunks, chunkX, chunkZ, minY));
        var entities = entry.GetProperty("blockEntities").EnumerateArray()
            .Select(VanillaFeaturePlacement.ExpectedBlockEntity).ToArray();
        Assert.Equal(entities, VanillaFeaturePlacement.BlockEntities(chunks, chunkX, chunkZ));
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
