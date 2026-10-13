using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Level stems with what vanilla's stem codec takes besides named noise generators: dimension types and biomes defined
/// inline, and <c>minecraft:flat</c> and <c>minecraft:debug</c> generators. Plugin generators named by a stem are
/// tested with the probe plugin (see <see cref="PluginLoading"/>).
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class LevelStemGenerators
{
    private const string FlatStem = """
        {
          "type": "minecraft:overworld",
          "generator": {
            "type": "minecraft:flat",
            "settings": {
              "layers": [
                { "block": "minecraft:bedrock", "height": 1 },
                { "block": "dirt", "height": 2 },
                { "block": "minecraft:not_a_block", "height": 1 },
                { "block": "minecraft:grass_block", "height": 1 }
              ],
              "biome": "minecraft:desert",
              "lakes": false,
              "features": false,
              "structure_overrides": []
            }
          }
        }
        """;

    [Fact(DisplayName = "A stem's inline dimension type and biomes are entries named for it, with ids")]
    public void InlineEntriesAreRegistered()
    {
        const string stem = """
            {
              "type": {
                "has_skylight": false,
                "has_ceiling": false,
                "coordinate_scale": 1,
                "min_y": 0,
                "height": 256,
                "logical_height": 256,
                "infiniburn": "#minecraft:infiniburn_overworld",
                "ambient_light": 0,
                "monster_spawn_light_level": 7,
                "monster_spawn_block_light_limit": 0
              },
              "generator": {
                "type": "minecraft:noise",
                "settings": "minecraft:nether",
                "biome_source": {
                  "type": "minecraft:checkerboard",
                  "biomes": [{BIOME}, "minecraft:plains", {BIOME}]
                }
              }
            }
            """;

        const string biome = """
            {
              "has_precipitation": false,
              "temperature": 2,
              "downfall": 0,
              "effects": { "water_color": "#3f76e4" },
              "carvers": [],
              "features": [],
              "spawners": {},
              "spawn_costs": {}
            }
            """;

        var bytes = Encoding.UTF8.GetBytes(stem.Replace("{BIOME}", biome));
        var (_, entries) = LevelStemInline.Extract("stemgen:ashes", bytes);

        Assert.Equal(
            [
                (PluginCodecs.DimensionType, "stemgen:ashes/type"),
                (PluginCodecs.Biome, "stemgen:ashes/biome_0"),
                (PluginCodecs.Biome, "stemgen:ashes/biome_1")
            ],
            entries.Select(entry => (entry.Registry, entry.Identifier)));

        // Read with the plugin's other files, they're registered like files of those identifiers.
        PluginCodecs.Apply(
            [.. entries.Select(entry => PluginCodecs.Parse(entry.Registry, entry.Identifier, "test", entry.Json))],
            NullLogger.Instance);

        var dimension = PluginDimensions.Parse("stemgen:ashes", "test", bytes).Dimension.Noise();

        Assert.Equal("stemgen:ashes/type", dimension.DimensionType.Name);
        Assert.Equal(256, dimension.Height);
        Assert.Equal(
            ["stemgen:ashes/biome_0", "minecraft:plains", "stemgen:ashes/biome_1"],
            dimension.SourceBiomes.Select(entry => entry.Name));
    }

    [Fact(DisplayName = "A flat stem's layers fill from the bottom of the level, in its biome")]
    public async Task FlatStemGeneratesLayers()
    {
        var stem = Assert.IsType<FlatStem>(PluginDimensions.Parse("stemgen:flat", "test", Bytes(FlatStem)).Dimension);
        var generator = stem.CreateGenerator();

        var chunk = await generator.GenerateChunkAsync(2, 3, new Chunk(2, 3, -64, 384));

        // Vanilla reads an unknown layer block as air.
        Assert.Equal((2, 3), (chunk.X, chunk.Z));
        Assert.True(chunk.IsGenerated);
        Assert.Equal(
            ["minecraft:bedrock", "minecraft:dirt", "minecraft:dirt", "minecraft:air", "minecraft:grass_block"],
            Enumerable.Range(-64, 5).Select(y => chunk.GetBlock(5, y, 9).UnlocalizedName));
        Assert.True(chunk.GetBlock(5, -59, 9).IsAir);
        Assert.Same(CodecRegistry.Biomes.Desert, chunk.GetBiome(5, 100, 9));
        Assert.Equal(new VectorD(0.5, -59, 0.5), await generator.FindSpawnPointAsync());
    }

    // What flat dimensions don't generate yet is refused, and so are generators other than vanilla's noise generator
    // for vanilla's own dimensions.
    [Theory(DisplayName = "Flat stems Obsidian can't generate as vanilla would are refused, saying where")]
    [InlineData("stemgen:lakes", "\"lakes\": false", "\"lakes\": true", "generator.settings.lakes")]
    [InlineData("stemgen:features", "\"features\": false", "\"features\": true", "generator.settings.features")]
    [InlineData("stemgen:structures", "\"structure_overrides\": []", "\"a\": 1", "structure_overrides")]
    [InlineData("stemgen:high", "\"height\": 2", "\"height\": 4063", "generator.settings.layers")]
    [InlineData("minecraft:overworld", "", "", "minecraft:noise")]
    public void UnsupportedFlatStemsAreRefused(string identifier, string from, string to, string message)
    {
        var json = from.Length > 0 ? FlatStem.Replace(from, to) : FlatStem;

        var ex = Assert.Throws<InvalidDataException>(() => PluginDimensions.Parse(identifier, "test", Bytes(json)));

        Assert.Contains(message, ex.Message);
    }

    [Fact(DisplayName = "A debug stem places every block state in vanilla's grid, over a barrier floor")]
    public async Task DebugStemPlacesStatesInGrid()
    {
        const string json = """{ "type": "minecraft:overworld", "generator": { "type": "minecraft:debug" } }""";

        var stem = Assert.IsType<DebugStem>(PluginDimensions.Parse("stemgen:debug", "test", Bytes(json)).Dimension);
        var generator = (DebugLevelGenerator)stem.CreateGenerator();
        var gridWidth = (int)MathF.Ceiling(MathF.Sqrt(BlocksRegistry.StateCount));

        // Vanilla's DebugLevelSource: state i is at x = 2 * (i / width) + 1, z = 2 * (i % width) + 1.
        (int X, int Z, int State)[] cells =
        [
            (1, 3, 1),
            (1, 5, 2),
            (3, 1, gridWidth),
            (3, 3, gridWidth + 1),
            (2, 3, 0),
            (-1, 3, 0)
        ];

        foreach (var (x, z, state) in cells)
            Assert.Equal(state, generator.GetBlockAt(x, z).GetHashCode());

        var chunk = await generator.GenerateChunkAsync(0, 0, new Chunk(0, 0, -64, 384));

        Assert.Equal("minecraft:barrier", chunk.GetBlock(4, 60, 4).UnlocalizedName);
        Assert.Equal(gridWidth + 1, chunk.GetBlock(3, 70, 3).GetHashCode());
        Assert.Same(CodecRegistry.Biomes.Plains, chunk.GetBiome(0, 70, 0));
    }

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);
}

/// <summary>
/// Reading dimensions in tests.
/// </summary>
internal static class LevelStemTestExtensions
{
    /// <summary>
    /// The dimension a stem generates with vanilla's noise generator, failing the test when it's another kind.
    /// </summary>
    public static MojangDimension Noise(this LevelStem? stem) => Assert.IsType<MojangDimension>(stem);
}
