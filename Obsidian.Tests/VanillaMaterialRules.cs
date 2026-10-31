using Obsidian.API;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Terrain;
using System;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Fast, one-block replays of vanilla's material rules with captured column and biome inputs.</summary>
public class VanillaMaterialRules
{
    [Fact]
    public void RulesAndContextsMatchVanilla()
    {
        var pipelines = new WorldgenPipelines();
        var cases = VanillaWorldgenCaptures.CurrentCases("materials");

        FixtureCases.AssertAll(cases, Name, entry => AssertRule(entry, pipelines));
    }

    private static string Name(JsonElement entry) => entry.GetProperty("name").GetString()!;

    private static void AssertRule(JsonElement entry, WorldgenPipelines pipelines)
    {
        var seed = entry.GetProperty("seed").GetInt64();
        var generator = pipelines.Terrain(seed, entry.GetProperty("settings").GetString()!);
        var x = entry.GetProperty("x").GetInt32();
        var y = entry.GetProperty("y").GetInt32();
        var z = entry.GetProperty("z").GetInt32();
        var biome = entry.GetProperty("biome").GetString()!;
        var chunk = new Chunk(x >> 4, z >> 4, generator.Settings.MinY, generator.Settings.Height);
        var heightmap = chunk.Heightmaps[HeightmapType.WorldSurfaceWG];

        // The probe supplies the two height differences directly to vanilla's context. The replay arranges the
        // same differences in its heightmap so the normal material context computes them through its public path.
        heightmap.Set((x - 1) & 15, z & 15, 64);
        heightmap.Set((x + 1) & 15, z & 15, 64 + entry.GetProperty("gradientX").GetInt32());
        heightmap.Set(x & 15, (z - 1) & 15, 64);
        heightmap.Set(x & 15, (z + 1) & 15, 64 + entry.GetProperty("gradientZ").GetInt32());
        var terrain = generator.CreateContext(chunk, (_, _, _) => biome);
        var volume = new DensityVolume(1, 1, 1, x, y, z);
        var context = new MaterialRules.MaterialContext(generator.Materials, terrain, terrain.Biomes.Surface, volume);
        context.UpdateColumn(x & 15, z & 15);
        context.UpdateBlock(
            y,
            entry.GetProperty("above").GetInt32(),
            entry.GetProperty("below").GetInt32(),
            entry.GetProperty("water").GetInt32());
        var rule = generator.Materials.CompileRule(entry.GetProperty("rule"));

        var block = rule(context);

        Assert.Equal(entry.GetProperty("surfaceDepth").GetInt32(), context.SurfaceDepth);
        Assert.Equal(entry.GetProperty("secondary").GetDouble(), context.Secondary);
        Assert.Equal(entry.GetProperty("minSurface").GetInt32(), context.MinSurface);

        if (entry.GetProperty("block").ValueKind == JsonValueKind.Null)
        {
            Assert.Null(block);
            return;
        }

        var expected = VanillaTerrainGeneration.ParseBlock(entry.GetProperty("block").GetString()!);
        Assert.NotNull(block);
        Assert.Equal(expected.RegistryId, block.RegistryId);
        Assert.Equal(expected.State?.Id ?? expected.DefaultId, block.State?.Id ?? block.DefaultId);
    }
}
