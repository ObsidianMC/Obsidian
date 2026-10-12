using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Dimension = Obsidian.WorldData.Dimension;

namespace Obsidian.Tests;

/// <summary>
/// Plugins' dimensions, from level stem files, generated with vanilla's noise generator.
/// </summary>
public sealed class PluginDimensionFiles : IDisposable
{
    private const string Ore = """
        {
          "type": "minecraft:ore",
          "config": {
            "size": 9,
            "discard_chance_on_air_exposure": 0,
            "targets": [
              {
                "target": { "predicate_type": "minecraft:tag_match", "tag": "minecraft:stone_ore_replaceables" },
                "state": { "Name": "minecraft:diamond_block" }
              }
            ]
          }
        }
        """;

    private const string PlacedOre = """
        {
          "feature": "stemfiles:ore_marker",
          "placement": [
            { "type": "minecraft:count", "count": 30 },
            { "type": "minecraft:in_square" },
            {
              "type": "minecraft:height_range",
              "height": {
                "type": "minecraft:uniform",
                "min_inclusive": { "absolute": 0 },
                "max_inclusive": { "absolute": 64 }
              }
            },
            { "type": "minecraft:biome" }
          ]
        }
        """;

    // A plugin biome placing the plugin's ore in the underground_ores step.
    private const string Glade = """
        {
          "has_precipitation": true,
          "temperature": 0.8,
          "downfall": 0.4,
          "effects": { "water_color": "#3f76e4" },
          "carvers": [],
          "features": [[], [], [], [], [], [], ["stemfiles:ore_marker"]],
          "spawners": {},
          "spawn_costs": {}
        }
        """;

    private const string FixedStem = """
        {
          "type": "minecraft:overworld",
          "generator": {
            "type": "minecraft:noise",
            "settings": "minecraft:overworld",
            "biome_source": { "type": "minecraft:fixed", "biome": "stemfiles:glade" }
          }
        }
        """;

    // Parameters as a number, [min, max] and {"min", "max"}, as vanilla takes them.
    private const string MultiNoiseStem = """
        {
          "type": "overworld",
          "generator": {
            "type": "noise",
            "settings": "overworld",
            "biome_source": {
              "type": "multi_noise",
              "biomes": [
                {
                  "biome": "stemfiles:glade",
                  "parameters": {
                    "temperature": [-1, 0],
                    "humidity": 0,
                    "continentalness": { "min": -0.11, "max": 1 },
                    "erosion": 0,
                    "depth": 0,
                    "weirdness": 0,
                    "offset": 0
                  }
                },
                {
                  "biome": "plains",
                  "parameters": {
                    "temperature": [0, 1],
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
          }
        }
        """;

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-plugin-dimensions-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(this.folder))
            Directory.Delete(this.folder, recursive: true);
    }

    [Fact(DisplayName = "A plugin's dimension generates its biome with its features, and is saved and reloaded")]
    public async Task GeneratesSavesAndReloads()
    {
        var logger = new PluginFeatureFiles.ListLogger();
        var glade = PluginCodecs.Parse(
            PluginCodecs.Biome,
            "stemfiles:glade",
            "test",
            Encoding.UTF8.GetBytes(Glade),
            id => id == "stemfiles:ore_marker");

        // Plugin biomes stay registered until the server stops, so other tests' entries don't mind this one.
        PluginCodecs.Apply([glade], logger);
        Assert.True(CodecRegistry.TryGetBiome("stemfiles:glade", out var gladeBiome));

        // Vanilla's dimensions can't be overridden, and what Obsidian can't generate yet is rejected, saying so.
        Assert.Throws<InvalidDataException>(() => Parse("minecraft:glade", FixedStem));
        var flat = Assert.Throws<InvalidDataException>(() => Parse(
            "stemfiles:flat",
            """{ "type": "overworld", "generator": { "type": "flat", "settings": {} } }"""));
        Assert.Contains("minecraft:flat", flat.Message);
        Assert.Throws<InvalidDataException>(() => Parse(
            "stemfiles:other",
            FixedStem.Replace("\"settings\": \"minecraft:overworld\"", "\"settings\": \"stemfiles:settings\"")));

        var multiNoise = Parse("stemfiles:meadow", MultiNoiseStem).Dimension;
        Assert.Equal(["stemfiles:glade", "minecraft:plains"], multiNoise.PossibleBiomes.Select(biome => biome.Name));

        var dimension = Parse("stemfiles:glade_world", FixedStem).Dimension;
        Assert.Equal("stemfiles:glade_world", dimension.Identifier);
        Assert.Same(CodecRegistry.Dimensions.All["minecraft:overworld"], dimension.DimensionType);

        // The biome's features are ordered across the dimension's biomes, as the server does for every dimension.
        var features = PluginFeatures.Build(
            [
                PluginFeatures.Parse(PluginFeatures.ConfiguredFeature, "stemfiles:ore_marker", "test", Bytes(Ore)),
                PluginFeatures.Parse(PluginFeatures.PlacedFeature, "stemfiles:ore_marker", "test", Bytes(PlacedOre))
            ],
            [glade],
            [],
            logger,
            [dimension]);

        Assert.Empty(logger.Messages);

        var builder = new ChunkBuilder(dimension, 1234, generateStructures: false, () => features);
        var chunk = new FullChunks(builder, dimension).Get(0, 0);

        Assert.Same(gladeBiome, chunk.GetBiome(0, 0, 0));
        Assert.True(PluginFeatureFiles.HasDiamondBlocks(chunk));

        // The level is saved where vanilla saves a data pack's dimension, and loads from there.
        var world = this.CreateWorld();
        var level = CreateLevel(world, dimension);

        chunk.SetChunkStatus(ChunkGenStage.full);
        ((Region)level.LoadRegion(0, 0)).SetChunk(chunk);
        await level.SaveAsync();

        await level.DisposeAsync();

        var region = Path.Join(this.folder, "dimensions", "stemfiles", "glade_world", "region", "r.0.0.mca");
        Assert.True(File.Exists(region));

        await using var reloaded = CreateLevel(world, dimension);
        Assert.True(await reloaded.LoadAsync(dimension.DimensionType));

        var saved = await reloaded.GetChunkAsync(0, 0, scheduleGeneration: false);
        Assert.NotNull(saved);
        Assert.Same(gladeBiome, saved.GetBiome(0, 0, 0));
        Assert.True(PluginFeatureFiles.HasDiamondBlocks(saved));
    }

    private static DimensionFile Parse(string identifier, string json) =>
        PluginDimensions.Parse(identifier, "test", Bytes(json));

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    private World CreateWorld()
    {
        var world = new World(
            NullLogger<World>.Instance,
            null!,
            null!,
            new TestOptionsMonitor<ServerConfiguration>(new ServerConfiguration()),
            null!,
            new EmptyWorldGenerator(),
            "world",
            "1234");

        world.UseVanillaLayout(this.folder);
        world.Initialize(CodecRegistry.Dimensions.All["minecraft:overworld"]);
        world.CreateVanillaLevel(new NbtCompound("Data") { new NbtTag<string>("LevelName", "world") });

        return world;
    }

    // The level the server creates for a plugin's dimension (see WorldManager.AddPluginDimensionsAsync).
    private static Dimension CreateLevel(World world, MojangDimension dimension)
    {
        var level = new Dimension(
            NullLogger<Dimension>.Instance,
            null!,
            new TestOptionsMonitor<ServerConfiguration>(new ServerConfiguration()),
            null!,
            new LevelStemGenerator(dimension),
            dimension.Identifier,
            world);

        level.Initialize(dimension.DimensionType);

        return level;
    }
}
