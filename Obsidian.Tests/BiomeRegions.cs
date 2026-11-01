using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang.Regions;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using TerrainGenerator = Obsidian.WorldData.Generators.Terrain.TerrainGenerator;

namespace Obsidian.Tests;

/// <summary>
/// Plugins' biome regions, which add biomes to a dimension's own layout. The vanilla replays and the worldgen bench
/// check that a dimension without regions generates exactly as before.
/// </summary>
public sealed class BiomeRegions
{
    // The crimson forest stands for a plugin biome: the overworld's own layout never gives it.
    private const string ReplacePlains = """
        {
          "dimension": "minecraft:overworld",
          "weight": 10,
          "replace": { "minecraft:plains": "minecraft:crimson_forest" }
        }
        """;

    // A region with a parameter list of its own: its single biome is the nearest everywhere.
    private const string WarpedEverywhere = """
        {
          "dimension": "minecraft:overworld",
          "weight": 10,
          "biomes": [
            {
              "biome": "minecraft:warped_forest",
              "parameters": {
                "temperature": 0,
                "humidity": 0,
                "continentalness": 0,
                "erosion": 0,
                "depth": 0,
                "weirdness": 0,
                "offset": 0
              }
            }
          ]
        }
        """;

    [Fact(DisplayName = "Regions give their biomes in their cells, in lookups and filled chunks")]
    public void RegionsGenerateInThe263Pipeline()
    {
        var replacing = Parse("test:crimson_plains", ReplacePlains);
        var parameterList = Parse("test:warped", WarpedEverywhere);
        ImmutableArray<BiomeRegion> regions = [replacing, parameterList];

        var terrain = new TerrainGenerator(WorldgenData.Vanilla, 12345L, "minecraft:overworld");
        var layout = BiomeLayout.Vanilla(WorldgenData.Vanilla, "minecraft:overworld");
        var vanilla = new ClimateSource(terrain.Router, layout);
        var placement = new RegionLayout("minecraft:overworld", regions, terrain.Compiler.Random);
        var source = new ClimateSource(terrain.Router, layout, placement);

        var columns = Enumerable.Range(0, 48)
            .SelectMany(x => Enumerable.Range(0, 48).Select(z => (X: x * 64, Z: z * 64)))
            .ToList();
        var seen = new HashSet<string>();

        foreach (var (x, z) in columns)
        {
            var region = placement.RegionAt(x, z);
            var expected = Expected(region, vanilla.GetNoiseBiome(x, 16, z).Name);
            Assert.Equal(expected, source.GetNoiseBiome(x, 16, z).Name);
            seen.Add(region?.Identifier ?? "dimension");
        }

        Assert.Equal(["dimension", "test:crimson_plains", "test:warped"], seen.Order());

        // Filling a chunk's biomes, as the biomes status does, gives the regions' biomes too.
        foreach (var (x, z) in columns.Take(64))
        {
            var chunkX = x >> 2;
            var chunkZ = z >> 2;
            var region = placement.RegionAt(chunkX << 2, chunkZ << 2);
            var own = vanilla.Stored(chunkX, chunkZ, -64, 384).Get(0, 20, 0).Name;

            Assert.Equal(Expected(region, own), source.Stored(chunkX, chunkZ, -64, 384).Get(0, 20, 0).Name);
        }

        // The regions' biomes follow the overworld's, in the regions' order.
        string[] possible = [.. vanilla.PossibleBiomes, "minecraft:crimson_forest", "minecraft:warped_forest"];
        Assert.Equal(possible, source.PossibleBiomes);
    }

    // The biome a region gives where the overworld's own layout gives the one named.
    private static string Expected(BiomeRegion? region, string own) => region?.Identifier switch
    {
        "test:warped" => "minecraft:warped_forest",
        "test:crimson_plains" when own == "minecraft:plains" => "minecraft:crimson_forest",
        _ => own
    };

    private static BiomeRegion Parse(string identifier, string json) =>
        PluginBiomeRegions.Parse(identifier, "test", Encoding.UTF8.GetBytes(json)).Region;

    [Theory]
    [InlineData("""{ "dimension": "minecraft:overworld", "weight": 1, "replace": {}, "extra": 1 }""", "extra")]
    [InlineData("""{ "dimension": "minecraft:overworld", "weight": 1 }""", "expected replace or biomes")]
    [InlineData("""{ "dimension": "minecraft:overworld", "weight": 0, "replace": {} }""", "weight")]
    [InlineData(
        """{ "dimension": "minecraft:overworld", "weight": 1, "replace": { "minecraft:plains": "test:none" } }""",
        "test:none isn't a biome")]
    [InlineData(
        """{ "dimension": "minecraft:overworld", "weight": 1, "replace": { "#Bad Tag": "minecraft:plains" } }""",
        "isn't an identifier or a tag")]
    public void InvalidRegionsAreRejected(string json, string message)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => PluginBiomeRegions.Parse("test:region", "test", Encoding.UTF8.GetBytes(json)));

        Assert.Contains(message, error.Message);
    }
}
