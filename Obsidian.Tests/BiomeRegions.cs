using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Regions;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plugins' biome regions, which add biomes to a dimension's own layout. Golden chunk tests check that a dimension
/// without regions generates exactly as before.
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

    [Fact]
    public void ReplacingRegionSwapsBiomesInSomeCellsDeterministically()
    {
        var region = PluginBiomeRegions.Parse("test:crimson_plains", "test", Encoding.UTF8.GetBytes(ReplacePlains));
        var settings = NoiseRegistry.NoiseSettings.All["minecraft:overworld"];

        IClimateBiomeSource Create(long seed)
        {
            var randomState = new RandomState(settings, seed);
            var overworld = MultiNoiseBiomeSource.Overworld(randomState);

            return RegionBiomeSource.Create("minecraft:overworld", overworld, [region.Region], randomState);
        }

        var vanilla = MultiNoiseBiomeSource.Overworld(new RandomState(settings, 12345L));
        var source = Create(12345L);
        var again = Create(12345L);

        var positions = Enumerable.Range(0, 64)
            .SelectMany(x => Enumerable.Range(0, 64).Select(z => (X: x * 64, Z: z * 64)))
            .ToList();

        var plains = 0;
        var replaced = 0;

        foreach (var (x, z) in positions)
        {
            var expected = vanilla.GetNoiseBiome(x, 16, z).Name;
            var biome = source.GetNoiseBiome(x, 16, z).Name;

            Assert.Equal(biome, again.GetNoiseBiome(x, 16, z).Name);

            if (expected != "minecraft:plains")
            {
                Assert.Equal(expected, biome);
                continue;
            }

            if (biome == "minecraft:crimson_forest")
                replaced++;
            else
                plains++;
        }

        Assert.True(replaced > 0, "The region never generated.");
        Assert.True(plains > 0, "The overworld's own layout never generated.");

        // Vanilla's biomes keep their order, which orders features, and the region's follow.
        string[] possible = [.. vanilla.PossibleBiomes.Select(biome => biome.Name), "minecraft:crimson_forest"];
        Assert.Equal(possible, source.PossibleBiomes.Select(biome => biome.Name));
    }

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
