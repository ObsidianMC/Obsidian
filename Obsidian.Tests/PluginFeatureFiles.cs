using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.WorldData.Generators.Mojang;
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
/// </remarks>
public class PluginFeatureFiles
{
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

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => this.Messages.Add(formatter(state, exception));
    }

    private static FeatureFile Parse(string registry, string identifier, string json) =>
        PluginFeatures.Parse(registry, identifier, "test", Encoding.UTF8.GetBytes(json));

    private static bool HasDiamondBlocks(IChunk chunk) =>
        Enumerable.Range(0, 16 * 16 * 128).Any(i => chunk.GetBlock(i & 15, (i >> 8) - 64, (i >> 4) & 15)
            .Is(BlockIds.DiamondBlock));
}
