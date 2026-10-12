using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Features;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Carvers;
using Obsidian.WorldData.Structures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plugins' configured and placed features and NeoForge biome modifiers, placed by world generation.
/// </summary>
/// <remarks>
/// Feature sets are built without becoming the current one, which tests loading plugins in parallel would replace.
/// Tags are global, so these tests run with the other tests applying tags.
/// </remarks>
[Collection(nameof(PluginTagFiles))]
public class PluginFeatureFiles
{
    // Vanilla's cave carver, starting in every chunk.
    private const string EveryChunkCaves = """
        {
          "type": "minecraft:cave",
          "config": {
            "probability": 1,
            "y": {
              "type": "minecraft:uniform",
              "min_inclusive": { "above_bottom": 8 },
              "max_inclusive": { "absolute": 60 }
            },
            "yScale": { "type": "minecraft:uniform", "min_inclusive": 0.1, "max_exclusive": 0.9 },
            "lava_level": { "above_bottom": 8 },
            "replaceable": "#minecraft:overworld_carver_replaceables",
            "horizontal_radius_multiplier": 1,
            "vertical_radius_multiplier": 1,
            "floor_level": -0.7
          }
        }
        """;

    // A placed feature tag in a modifier, and spawns added and removed with an entity type tag.
    private const string AddTaggedOres = """
        {
          "type": "neoforge:add_features",
          "biomes": "minecraft:plains",
          "features": "#featurefiles:ores",
          "step": "underground_ores"
        }
        """;

    // Bees are in no vanilla biome's spawners, so only their type says they're creatures.
    private const string AddPolarBears = """
        {
          "type": "neoforge:add_spawns",
          "biomes": "minecraft:plains",
          "spawners": [
            { "type": "minecraft:polar_bear", "weight": 5, "minCount": 1, "maxCount": 2 },
            { "type": "minecraft:bee", "weight": 5, "minCount": 1, "maxCount": 2 }
          ]
        }
        """;

    private const string RemoveSkeletons = """
        {
          "type": "neoforge:remove_spawns",
          "biomes": ["minecraft:plains", "minecraft:swamp"],
          "entity_types": "#minecraft:skeletons"
        }
        """;

    // Overworld biomes get the carver, except the deep dark, so start chunks' biomes decide.
    private const string AddCaves = """
        {
          "type": "neoforge:add_carvers",
          "biomes": "#minecraft:is_overworld",
          "carvers": "featurefiles:every_chunk_caves"
        }
        """;

    private const string RemoveCaves = """
        {
          "type": "neoforge:remove_carvers",
          "biomes": "minecraft:deep_dark",
          "carvers": ["featurefiles:every_chunk_caves"]
        }
        """;

    // Diamond blocks, which vanilla never generates without structures, in place of stone.
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
          "feature": "featurefiles:ore_marker",
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

    private const string AddOre = """
        {
          "type": "neoforge:add_features",
          "biomes": "#minecraft:is_overworld",
          "features": "featurefiles:ore_marker",
          "step": "underground_ores"
        }
        """;

    private const string RemoveCoal = """
        {
          "type": "neoforge:remove_features",
          "biomes": ["minecraft:plains"],
          "features": "minecraft:ore_coal_upper",
          "steps": "underground_ores"
        }
        """;

    private const string NoOp = """{ "feature": { "type": "minecraft:no_op", "config": {} }, "placement": [] }""";

    // Plains places a before b, and forest and nether wastes b before a.
    private const string AddAB = """
        {
          "type": "neoforge:add_features",
          "biomes": "minecraft:plains",
          "features": ["featurefiles:a", "featurefiles:b"],
          "step": "vegetal_decoration"
        }
        """;

    private const string AddBA = """
        {
          "type": "neoforge:add_features",
          "biomes": ["minecraft:forest", "minecraft:nether_wastes"],
          "features": ["featurefiles:b", "featurefiles:a"],
          "step": "vegetal_decoration"
        }
        """;

    private const string RemoveA = """
        {
          "type": "neoforge:remove_features",
          "biomes": "minecraft:forest",
          "features": "featurefiles:a"
        }
        """;

    // A plugin biome with a placed feature defined inline, written leniently like data packs can be, and the plugin's
    // ore.
    private const string Marsh = """
        {
          "has_precipitation": true,
          "temperature": 0.5,
          "downfall": 0.5,
          "effects": { "water_color": "#3f76e4" },
          "carvers": [],
          "features": [
            [],
            [
              { "feature": { "type": "minecraft:no_op", "config": {} /* off */, }, "placement": [] },
              "featurefiles:ore_marker"
            ]
          ],
          "spawners": {},
          "spawn_costs": {}
        }
        """;

    [Fact(DisplayName = "A plugin's ore added to vanilla biomes by a biome modifier generates, and goes with it")]
    public void AddsFeaturesWithBiomeModifiers()
    {
        // Plugins can't replace vanilla's features.
        Assert.Throws<InvalidDataException>(() => Parse(PluginFeatures.PlacedFeature, "minecraft:ore_iron", PlacedOre));

        FeatureFile[] files =
        [
            Parse(PluginFeatures.ConfiguredFeature, "featurefiles:ore_marker", Ore),
            Parse(PluginFeatures.PlacedFeature, "featurefiles:ore_marker", PlacedOre),
            Parse(PluginFeatures.BiomeModifier, "featurefiles:remove_coal", RemoveCoal),
            Parse(PluginFeatures.BiomeModifier, "featurefiles:add_ore", AddOre),
            Parse(PluginFeatures.PlacedFeature, "featurefiles:a", NoOp),
            Parse(PluginFeatures.PlacedFeature, "featurefiles:b", NoOp),
            Parse(PluginFeatures.BiomeModifier, "featurefiles:add_ab", AddAB),
            Parse(PluginFeatures.BiomeModifier, "featurefiles:add_ba", AddBA),
            Parse(PluginFeatures.BiomeModifier, "featurefiles:remove_a", RemoveA)
        ];

        var marsh = PluginCodecs.Parse(
            PluginCodecs.Biome,
            "featurefiles:marsh",
            "test",
            Encoding.UTF8.GetBytes(Marsh),
            id => id == "featurefiles:ore_marker" || PluginFeatures.IsPlacedFeature(id));

        var logger = new ListLogger();
        var current = PluginFeatures.Build(files, [marsh], [], logger);
        Assert.Empty(logger.Messages);

        var ore = current.PlacedFeatures["featurefiles:ore_marker"];
        var coal = PlacedFeatures.All["minecraft:ore_coal_upper"];
        var ores = (int)DecorationStep.UndergroundOres;

        Assert.Same(current.ConfiguredFeatures["featurefiles:ore_marker"], ore.Feature);
        Assert.Contains(ore, current.BiomeFeatures["minecraft:plains"][ores]);
        Assert.Contains(ore, current.BiomeFeatures["minecraft:dripstone_caves"][ores]);
        Assert.DoesNotContain(ore, current.BiomeFeatures["minecraft:nether_wastes"][ores]);

        // Removing runs after adding, whatever the load order, like NeoForge's phases, and only where it's asked to.
        Assert.DoesNotContain(coal, current.BiomeFeatures["minecraft:plains"][ores]);
        Assert.Contains(coal, current.BiomeFeatures["minecraft:forest"][ores]);

        var marshSteps = current.BiomeFeatures["featurefiles:marsh"];
        Assert.Equal("minecraft:no_op", marshSteps[1][0].Feature.Type);
        Assert.Same(ore, marshSteps[1][1]);

        // Features are ordered once every modifier ran, and per dimension: removing a from forest resolves the
        // overworld's cycle, and the nether's order is its own.
        var a = current.PlacedFeatures["featurefiles:a"];
        var b = current.PlacedFeatures["featurefiles:b"];
        var vegetal = (int)DecorationStep.VegetalDecoration;

        Assert.Equal([a, b], current.BiomeFeatures["minecraft:plains"][vegetal].TakeLast(2));
        Assert.Equal([b], current.BiomeFeatures["minecraft:forest"][vegetal].TakeLast(1));
        Assert.Equal([b, a], current.BiomeFeatures["minecraft:nether_wastes"][vegetal].TakeLast(2));

        // Without the removal, the overworld can't be ordered and is generated without the modifiers.
        var cycle = PluginFeatures.Build(files[..^1], [], [], logger);
        var cycleA = cycle.PlacedFeatures["featurefiles:a"];

        Assert.Single(logger.Messages);
        Assert.DoesNotContain(cycleA, cycle.BiomeFeatures["minecraft:plains"][vegetal]);
        Assert.Contains(cycleA, cycle.BiomeFeatures["minecraft:nether_wastes"][vegetal]);

        var builder = new ChunkBuilder(MojangDimension.Overworld, 1234, generateStructures: false, () => current);
        var chunks = new FullChunks(builder, MojangDimension.Overworld);
        Assert.True(HasDiamondBlocks(chunks.Get(0, 0)));

        // Once the plugin is gone, vanilla's features are back, and the builder places them in new chunks.
        current = PluginFeatures.Build([], [], [], NullLogger.Instance);

        Assert.Same(BiomeFeatures.All, current.BiomeFeatures);
        Assert.Empty(current.PlacedFeatures);
        Assert.False(HasDiamondBlocks(chunks.Get(8, 8)));
    }

    [Fact(DisplayName = "A plugin's registered feature and placement modifier types generate, and go with it")]
    public void PlacesRegisteredWorldgenTypes()
    {
        // The types the probe plugin registers, here from the test's own load context.
        List<WorldgenTypeRegistration> types = [];
        types.Add(WorldgenTypeRegistration.Create(
            types,
            "probe:pillar",
            typeof(ConfiguredFeatureBase),
            typeof(ProbePillarFeature)));
        types.Add(WorldgenTypeRegistration.Create(
            types,
            "probe:at_height",
            typeof(PlacementModifierBase),
            typeof(ProbeAtHeight)));

        FeatureFile[] files =
        [
            Parse(PluginFeatures.ConfiguredFeature, "probe:pillar", ProbeData("configured_feature/pillar.json")),
            Parse(PluginFeatures.PlacedFeature, "probe:pillar", ProbeData("placed_feature/pillar.json")),
            Parse(PluginFeatures.BiomeModifier, "probe:add_pillar", PluginLoading.AddPillar)
        ];

        var logger = new ListLogger();
        var current = PluginFeatures.Build(files, [], types, logger);
        Assert.Empty(logger.Messages);

        // Their properties are read like vanilla types': a number is a constant int provider, and a block set a list
        // of blocks and tags.
        var pillar = current.PlacedFeatures["probe:pillar"];
        var feature = Assert.IsType<ProbePillarFeature>(pillar.Feature);

        Assert.Equal(8, feature.Height.MaxValue);
        Assert.Equal(["minecraft:stone", "#minecraft:deepslate_ore_replaceables"], feature.Replaceable.Entries);
        Assert.Equal(-4, Assert.IsType<ProbeAtHeight>(pillar.Placement[0]).Y);

        var builder = new ChunkBuilder(MojangDimension.Overworld, 1234, generateStructures: false, () => current);
        var chunks = new FullChunks(builder, MojangDimension.Overworld);
        Assert.True(HasDiamondBlocks(chunks.Get(0, 0)));

        // Once the plugin is gone, so are its types and the features made of them.
        current = PluginFeatures.Build(files, [], [], logger);

        Assert.Contains(logger.Messages, message => message.Contains("probe:pillar isn't a feature type"));
        Assert.Empty(current.PlacedFeatures);
        Assert.Same(BiomeFeatures.All, current.BiomeFeatures);
        Assert.False(HasDiamondBlocks(chunks.Get(8, 8)));
    }

    [Fact(DisplayName = "Spawn and carver modifiers and placed feature tags change vanilla biomes, and go with them")]
    public void AppliesSpawnAndCarverModifiersAndFeatureTags()
    {
        FeatureFile[] files =
        [
            Parse(PluginFeatures.ConfiguredFeature, "featurefiles:ore_marker", Ore),
            Parse(PluginFeatures.PlacedFeature, "featurefiles:ore_marker", PlacedOre),
            Parse(PluginFeatures.ConfiguredCarver, "featurefiles:every_chunk_caves", EveryChunkCaves),
            Parse(PluginFeatures.BiomeModifier, "featurefiles:add_tagged_ores", AddTaggedOres),
            Parse(PluginFeatures.BiomeModifier, "featurefiles:remove_caves", RemoveCaves),
            Parse(PluginFeatures.BiomeModifier, "featurefiles:add_caves", AddCaves),
            Parse(PluginFeatures.BiomeModifier, "featurefiles:remove_skeletons", RemoveSkeletons),
            Parse(PluginFeatures.BiomeModifier, "featurefiles:add_polar_bears", AddPolarBears)
        ];

        var tagPath = "data/featurefiles/tags/worldgen/placed_feature/ores.json";
        var (_, registry, identifier) = Assert.Single(PluginTags.FindFiles([tagPath]));
        var ores = PluginTags.Parse(registry, identifier, "test", """{ "values": ["featurefiles:ore_marker"] }"""u8);

        try
        {
            PluginTags.Apply([ores], NullLogger.Instance, files.Select(file => (file.Registry, file.Identifier)));

            var logger = new ListLogger();
            var current = PluginFeatures.Build(files, [], [], logger);
            var spawns = PluginSpawns.Build(files, [], logger);
            Assert.Empty(logger.Messages);

            var ore = current.PlacedFeatures["featurefiles:ore_marker"];
            Assert.Contains(ore, current.BiomeFeatures["minecraft:plains"][(int)DecorationStep.UndergroundOres]);

            // Carvers are added to the end of the biomes' lists, after every removal ran.
            var caves = current.ConfiguredCarvers["featurefiles:every_chunk_caves"];
            Assert.Equal(caves, current.BiomeCarvers["minecraft:plains"][^1]);
            Assert.Equal(4, current.BiomeCarvers["minecraft:plains"].Length);
            Assert.Equal(3, current.BiomeCarvers["minecraft:deep_dark"].Length);

            // Spawners go to their type's category's list.
            var plains = spawns.Biomes["minecraft:plains"];
            Assert.Equal(["minecraft:polar_bear", "minecraft:bee"], plains["creature"][^2..].Select(mob => mob.Type));
            Assert.DoesNotContain(plains["monster"], spawner => spawner.Type == "minecraft:skeleton");
            Assert.DoesNotContain(
                spawns.Biomes["minecraft:swamp"]["monster"],
                spawner => spawner.Type == "minecraft:bogged");
            Assert.Contains(
                spawns.Biomes["minecraft:forest"]["monster"],
                spawner => spawner.Type == "minecraft:skeleton");

            // Each start chunk carves with its biome's carvers.
            var builder = new ChunkBuilder(MojangDimension.Overworld, 1234, generateStructures: false, () => current);
            var vanilla = new ChunkBuilder(MojangDimension.Overworld, 1234, generateStructures: false);
            Assert.True(CarvedAir(builder) > CarvedAir(vanilla));

            // Once the plugin is gone, vanilla's spawns and carvers are back.
            Assert.Same(BiomeSpawns.Vanilla, PluginSpawns.Build([], [], logger));
            Assert.Same(ConfiguredCarvers.VanillaBiomes, PluginFeatures.Build([], [], [], logger).BiomeCarvers);
        }
        finally
        {
            PluginTags.Apply([], NullLogger.Instance);
        }
    }

    // The air in chunk (0, 0) below Y 60 once carved.
    private static int CarvedAir(ChunkBuilder builder)
    {
        var chunk = new Chunk(0, 0, MojangDimension.Overworld.MinY, MojangDimension.Overworld.Height);
        builder.PopulateBiomes(chunk);
        builder.Generate3DTerrain(chunk);
        builder.ApplySurfaceRules(chunk);
        builder.ApplyCarvers(chunk);

        return Enumerable.Range(0, 16 * 16 * 124)
            .Count(i => chunk.GetBlock(i & 15, (i >> 8) - 64, (i >> 4) & 15).IsAir);
    }

    internal class ListLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => this.Messages.Add(formatter(state, exception));
    }

    internal sealed class ListLogger<T> : ListLogger, ILogger<T>;

    private static FeatureFile Parse(string registry, string identifier, string json) =>
        PluginFeatures.Parse(registry, identifier, "test", Encoding.UTF8.GetBytes(json));

    private static string ProbeData(string path) =>
        File.ReadAllText(Path.Join(AppContext.BaseDirectory, "data", "probe", "worldgen", path));

    internal static bool HasDiamondBlocks(IChunk chunk) =>
        Enumerable.Range(0, 16 * 16 * 128).Any(i => chunk.GetBlock(i & 15, (i >> 8) - 64, (i >> 4) & 15)
            .Is(BlockIds.DiamondBlock));
}
