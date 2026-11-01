using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Features;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Terrain.Carvers;
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
[Trait("Category", TestCategory.Worldgen)]
[Collection(nameof(PluginTagFiles))]
public class PluginFeatureFiles
{
    // 26.3 geometry, starting caves in every chunk; fluids and replaceability belong to mask application.
    internal const string EveryChunkCaves = """
        {
          "type": "minecraft:cave",
          "probability": 1,
          "y": {
            "type": "minecraft:uniform",
            "min_inclusive": { "above_bottom": 8 },
            "max_inclusive": { "absolute": 60 }
          },
          "count": 12,
          "thickness": 2,
          "room_vertical_radius_multiplier": 0.5,
          "horizontal_radius_multiplier": 1,
          "vertical_radius_multiplier": 1,
          "floor_level": -0.7
        }
        """;

    // A placed feature tag in a modifier, and spawns added and removed with an entity type tag.
    private const string AddTaggedOres = """
        {
          "biomes": "minecraft:plains",
          "add": { "features": { "underground_ores": "#featurefiles:ores" } }
        }
        """;

    // Bees are in no vanilla biome's spawners, so only their type says they're creatures.
    private const string AddPolarBears = """
        {
          "biomes": "minecraft:plains",
          "add": {
            "spawners": [
              { "type": "minecraft:polar_bear", "weight": 5, "minCount": 1, "maxCount": 2 },
              { "type": "minecraft:bee", "weight": 5, "minCount": 1, "maxCount": 2 }
            ],
            "spawn_costs": { "minecraft:bee": { "energy_budget": 0.12, "charge": 1.0 } }
          }
        }
        """;

    private const string RemoveSkeletons = """
        {
          "biomes": ["minecraft:plains", "minecraft:swamp", "#minecraft:is_nether"],
          "exclude": "minecraft:warped_forest",
          "remove": { "spawners": "#minecraft:skeletons", "spawn_costs": ["minecraft:enderman"] }
        }
        """;

    // Overworld biomes get the carver, except the deep dark, so start chunks' biomes decide.
    private const string AddCaves = """
        {
          "biomes": "#minecraft:is_overworld",
          "add": { "carvers": "featurefiles:every_chunk_caves" }
        }
        """;

    private const string RemoveCaves = """
        {
          "biomes": "minecraft:deep_dark",
          "remove": { "carvers": ["featurefiles:every_chunk_caves"] }
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
          "biomes": "#minecraft:is_overworld",
          "add": { "features": { "underground_ores": "featurefiles:ore_marker" } }
        }
        """;

    private const string RemoveCoal = """
        {
          "biomes": ["minecraft:plains"],
          "remove": { "features": { "underground_ores": "minecraft:ore_coal_upper" } }
        }
        """;

    private const string NoOp = """{ "feature": { "type": "minecraft:no_op", "config": {} }, "placement": [] }""";

    // Plains places a before b, and forest and nether wastes b before a.
    private const string AddAB = """
        {
          "biomes": "minecraft:plains",
          "add": { "features": { "vegetal_decoration": ["featurefiles:a", "featurefiles:b"] } }
        }
        """;

    private const string AddBA = """
        {
          "biomes": ["minecraft:forest", "minecraft:nether_wastes"],
          "add": { "features": { "vegetal_decoration": ["featurefiles:b", "featurefiles:a"] } }
        }
        """;

    private const string RemoveA = """
        {
          "biomes": "minecraft:forest",
          "remove": { "features": "featurefiles:a" }
        }
        """;

    // A plugin biome with a placed feature defined inline, written leniently like data packs can be, and its ore.
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
        var current = PluginFeatures.BuildRegistry(files, [marsh], [], logger).FeatureSet!;
        Assert.Empty(logger.Messages);

        var ore = current.PlacedFeatures["featurefiles:ore_marker"];
        var coal = current.FindPlaced("minecraft:ore_coal_upper");
        var ores = (int)DecorationStep.UndergroundOres;

        Assert.Contains(ore, current.BiomeFeatures["minecraft:plains"][ores]);
        Assert.Contains(ore, current.BiomeFeatures["minecraft:dripstone_caves"][ores]);
        Assert.DoesNotContain(ore, current.BiomeFeatures["minecraft:nether_wastes"][ores]);

        // Removing runs after adding, whatever the load order, like NeoForge's phases, and only where it's asked to.
        Assert.DoesNotContain(coal, current.BiomeFeatures["minecraft:plains"][ores]);
        Assert.Contains(coal, current.BiomeFeatures["minecraft:forest"][ores]);

        // The marsh's inline feature, then the plugin's ore by identifier.
        var marshSteps = current.BiomeFeatures["featurefiles:marsh"];
        Assert.Equal(2, marshSteps[1].Count);
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
        var cycle = PluginFeatures.BuildRegistry(files[..^1], [], [], logger).FeatureSet!;
        var cycleA = cycle.PlacedFeatures["featurefiles:a"];

        Assert.Single(logger.Messages);
        Assert.DoesNotContain(cycleA, cycle.BiomeFeatures["minecraft:plains"][vegetal]);
        Assert.Contains(cycleA, cycle.BiomeFeatures["minecraft:nether_wastes"][vegetal]);

        var native = PluginFeatures.BuildRegistry(files, [], types: [], NullLogger.Instance);
        var builder = BiomeTerrain.Create(WorldgenDimension.Overworld, 1234,
            featureSource: () => native);
        var chunks = new NativeChunks(builder);
        Assert.True(HasDiamondBlocks(chunks.Get(0, 0)));

        // Once the plugin is gone, vanilla's features are back, and the builder places them in new chunks.
        native = PluginFeatures.BuildRegistry([], [], [], NullLogger.Instance);
        current = native.FeatureSet!;

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
        var native = PluginFeatures.BuildRegistry(files, [], types, logger);
        var current = native.FeatureSet!;
        Assert.Empty(logger.Messages);

        // Their properties are read like vanilla types': a number is a constant int provider, and a block set a list
        // of blocks and tags. The placement modifier places the pillar where it generates.
        var feature = Assert.IsType<ProbePillarFeature>(current.FindConfigured("probe:pillar"));

        Assert.Equal(8, feature.Height.MaxValue);
        Assert.Equal(["minecraft:stone", "#minecraft:deepslate_ore_replaceables"], feature.Replaceable.Entries);

        var builder = BiomeTerrain.Create(WorldgenDimension.Overworld, 1234,
            featureSource: () => native);
        var chunks = new NativeChunks(builder);
        Assert.True(HasDiamondBlocks(chunks.Get(0, 0)));

        // Once the plugin is gone, so are its types and the features made of them.
        native = PluginFeatures.BuildRegistry(files, [], [], logger);
        current = native.FeatureSet!;

        Assert.Contains(logger.Messages, message => message.Contains("probe:pillar isn't a valid feature"));
        Assert.Empty(current.PlacedFeatures);
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
            var registry263 = PluginFeatures.BuildRegistry(files, [], [], logger);
            var current = registry263.FeatureSet!;
            var spawns = PluginSpawns.Build(files, [], logger);
            Assert.Empty(logger.Messages);

            var ore = current.PlacedFeatures["featurefiles:ore_marker"];
            Assert.Contains(ore, current.BiomeFeatures["minecraft:plains"][(int)DecorationStep.UndergroundOres]);

            // Carvers are added to the end of the biomes' lists, after every removal ran.
            var carvers = registry263.Carvers;
            Assert.Equal("featurefiles:every_chunk_caves", carvers.BiomeCarvers("minecraft:plains")[^1]);
            Assert.DoesNotContain("featurefiles:every_chunk_caves", carvers.BiomeCarvers("minecraft:deep_dark"));

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
            Assert.DoesNotContain(
                spawns.Biomes["minecraft:soul_sand_valley"]["monster"],
                spawner => spawner.Type == "minecraft:skeleton");

            // Spawn costs are added, and removed from every biome but the excluded one.
            Assert.Equal(0.12, spawns.CostOf("minecraft:plains", "minecraft:bee")?.EnergyBudget);
            Assert.Null(spawns.CostOf("minecraft:soul_sand_valley", "minecraft:enderman"));
            Assert.NotNull(spawns.CostOf("minecraft:warped_forest", "minecraft:enderman"));
            Assert.Equal(0.07f, spawns.CreatureProbabilityOf("minecraft:snowy_plains"));

            // Each start chunk carves with its biome's carvers.
            var builder = CarverTerrain(() => carvers);
            var vanillaCarvers = PluginFeatures.BuildRegistry([], [], [], NullLogger.Instance).Carvers;
            var vanilla = CarverTerrain(() => vanillaCarvers);
            Assert.True(CarvedAir(builder) > CarvedAir(vanilla));

            // Once the plugin is gone, vanilla's spawns and carvers are back.
            Assert.Same(BiomeSpawns.Vanilla, PluginSpawns.Build([], [], logger));
            carvers = vanillaCarvers;
            Assert.Equal(vanilla.CarversAt(0, 0), builder.CarversAt(0, 0));
            Assert.Equal(CarvedAir(vanilla), CarvedAir(builder));
        }
        finally
        {
            PluginTags.Apply([], NullLogger.Instance);
        }
    }

    internal static BiomeTerrain CarverTerrain(Func<CarverRegistry> carvers, string biome = "minecraft:plains")
    {
        var stem = $$"""
            {
              "type": "minecraft:overworld",
              "generator": {
                "type": "minecraft:noise",
                "settings": "minecraft:overworld",
                "biome_source": { "type": "minecraft:fixed", "biome": "{{biome}}" }
              }
            }
            """;
        var dimension = WorldgenDimension.Read(
            "featurefiles:carvers", Encoding.UTF8.GetBytes(stem), WorldgenNoise.Vanilla,
            WorldgenData.Vanilla, carvers: carvers);
        return BiomeTerrain.Create(dimension, 1234);
    }

    [Fact]
    public void Resolves26_3CarverFilesInlineBiomesAndNestedTags()
    {
        var file = Parse(PluginFeatures.Carver, "featurefiles:caves", EveryChunkCaves);
        var inner = PluginTags.Parse(PluginTags.ConfiguredCarver, "featurefiles:inner", "inner",
            """{ "values": ["featurefiles:caves"] }"""u8);
        var (_, tagRegistry, tagId) = Assert.Single(PluginTags.FindFiles(
            ["data/featurefiles/tags/worldgen/carver/outer.json"]));
        var outer = PluginTags.Parse(tagRegistry, tagId, "outer",
            """{ "values": ["#featurefiles:inner"] }"""u8);
        var biomeJson = $$"""
            {
              "has_precipitation": true,
              "temperature": 0.5,
              "downfall": 0.5,
              "effects": { "water_color": "#3f76e4" },
              "carvers": [{{EveryChunkCaves}}, "#featurefiles:outer", "minecraft:cave"],
              "features": []
            }
            """;
        var biome = PluginCodecs.Parse(PluginCodecs.Biome, "minecraft:plains", "inline-plugin",
            Encoding.UTF8.GetBytes(biomeJson), _ => true, _ => true);

        try
        {
            PluginTags.Apply([inner, outer], NullLogger.Instance, [(file.Registry, file.Identifier)]);
            var logger = new ListLogger();
            var registry = PluginFeatures.BuildRegistry([file], [biome], [], logger).Carvers;
            var terrain = CarverTerrain(() => registry);
            var ids = terrain.CarversAt(0, 0);

            Assert.Empty(logger.Messages);
            Assert.Equal(3, ids.Length);
            Assert.Equal(["featurefiles:caves", "minecraft:cave"], ids[1..]);
            Assert.Equal(1, registry.Configuration(ids[0]).Probability);
            Assert.NotSame(registry.Configuration(ids[0]), registry.Configuration(ids[1]));
            Assert.True(CarvedAir(terrain) > CarvedAir(CarverTerrain(() =>
                PluginFeatures.BuildRegistry([], [], [], NullLogger.Instance).Carvers)));

            // Required tag cycles are rejected before carvers resolve them, without recursion in the reader.
            var cycle = PluginTags.Parse(PluginTags.ConfiguredCarver, "featurefiles:inner", "cycle-plugin",
                """{ "values": ["#featurefiles:outer"] }"""u8);
            PluginTags.Apply([cycle, outer], logger, [(file.Registry, file.Identifier)]);
            var invalid = PluginFeatures.BuildRegistry([file], [biome], [], logger).Carvers;
            Assert.DoesNotContain("featurefiles:caves", invalid.BiomeCarvers("minecraft:plains"));
            Assert.Contains(logger.Messages, message => message.Contains("inline-plugin"));
        }
        finally
        {
            PluginTags.Apply([], NullLogger.Instance);
        }
    }

    [Fact]
    public void Reloads26_3CarverOverridesAndSkipsMalformedFilesPerPlugin()
    {
        var quiet = EveryChunkCaves.Replace("\"probability\": 1", "\"probability\": 0");
        var valid = Parse(PluginFeatures.ConfiguredCarver, "minecraft:cave", EveryChunkCaves)
            with { Source = "valid-plugin/cave.json" };
        var malformed = Parse(PluginFeatures.ConfiguredCarver, "minecraft:cave", """{ "type": "minecraft:cave" }""")
            with { Source = "broken-plugin/cave.json" };
        var unrelated = Parse(PluginFeatures.ConfiguredCarver, "featurefiles:good", EveryChunkCaves);
        var addInline = Parse(PluginFeatures.BiomeModifier, "featurefiles:inline", $$"""
            { "biomes": "minecraft:plains", "add": { "carvers": {{EveryChunkCaves}} } }
            """);
        var brokenModifier = Parse(PluginFeatures.BiomeModifier, "featurefiles:broken", """
            {
              "biomes": "minecraft:plains",
              "add": { "carvers": "featurefiles:missing", "features": { "lakes": "minecraft:lake_lava" } }
            }
            """) with { Source = "broken-plugin/modifier.json" };
        var logger = new ListLogger();
        var registry = PluginFeatures.BuildRegistry([valid, unrelated, addInline], [], [], logger).Carvers;
        var terrain = CarverTerrain(() => registry);
        var activeAir = CarvedAir(terrain);
        Assert.StartsWith("obsidian:inline_carver/", terrain.CarversAt(0, 0)[^1]);

        // The same identifier now names different geometry; an existing generator must drop its old list/cache.
        registry = PluginFeatures.BuildRegistry([valid with { Json = Parse(
            PluginFeatures.ConfiguredCarver, "minecraft:cave", quiet).Json }], [], [], logger).Carvers;
        Assert.DoesNotContain(terrain.CarversAt(0, 0), id => id.StartsWith("obsidian:inline_carver/"));
        Assert.True(activeAir > CarvedAir(terrain));

        // A malformed override falls back to vanilla. Other files survive, and broken modifiers apply nothing.
        var broken = PluginFeatures.BuildRegistry([malformed, unrelated, brokenModifier], [], [], logger);
        var vanilla = PluginFeatures.BuildRegistry([], [], [], NullLogger.Instance);
        Assert.Equal(vanilla.Carvers.Configuration("minecraft:cave").Probability,
            broken.Carvers.Configuration("minecraft:cave").Probability);
        Assert.Equal(1, broken.Carvers.Configuration("featurefiles:good").Probability);
        Assert.DoesNotContain("featurefiles:missing", broken.Carvers.BiomeCarvers("minecraft:plains"));
        Assert.Equal(vanilla.FeatureSet!.BiomeFeatures["minecraft:plains"][1].Count,
            broken.FeatureSet!.BiomeFeatures["minecraft:plains"][1].Count);
        Assert.Contains(logger.Messages, message => message.Contains("broken-plugin/cave.json"));
        Assert.Contains(logger.Messages, message => message.Contains("broken-plugin/modifier.json"));

        registry = vanilla.Carvers;
        Assert.Equal(vanilla.Carvers.BiomeCarvers("minecraft:plains"), terrain.CarversAt(0, 0));
    }

    [Fact]
    public void ReloadsPluginUncarvableBlocksWith26_3Carvers()
    {
        var file = Parse(PluginFeatures.Carver, "minecraft:cave", EveryChunkCaves);
        var carvers = PluginFeatures.BuildRegistry([file], [], [], NullLogger.Instance).Carvers;
        var terrain = CarverTerrain(() => carvers);
        var carved = CarvedAir(terrain);
        var tag = PluginTags.Parse("block", "minecraft:uncarvable", "carver-plugin", """
            { "values": ["minecraft:stone", "minecraft:deepslate", "minecraft:dirt", "minecraft:grass_block"] }
            """u8);

        try
        {
            PluginTags.Apply([tag], NullLogger.Instance);
            carvers = PluginFeatures.BuildRegistry([file], [], [], NullLogger.Instance).Carvers;
            Assert.True(CarvedAir(terrain) < carved);

            PluginTags.Apply([], NullLogger.Instance);
            carvers = PluginFeatures.BuildRegistry([file], [], [], NullLogger.Instance).Carvers;
            Assert.Equal(carved, CarvedAir(terrain));
        }
        finally
        {
            PluginTags.Apply([], NullLogger.Instance);
        }
    }

    [Fact]
    public void Applied26_3CarversResolveAcrossPluginsAndRefreshExistingGenerators()
    {
        var file = Parse(PluginFeatures.Carver, "featurefiles:caves", EveryChunkCaves);
        var modifier = Parse(PluginFeatures.BiomeModifier, "featurefiles:caves", """
            { "biomes": "minecraft:plains", "add": { "carvers": "featurefiles:caves" } }
            """);

        try
        {
            Assert.True(PluginFeatures.Apply([file, modifier], [], [], new HashSet<string>(), NullLogger.Instance));
            Assert.True(PluginFeatures.IsConfiguredCarver(file.Identifier));

            // A later plugin's biome is parsed before its files join the native snapshot. Its loader must see
            // native carvers even though the live legacy reader cannot read their 26.3 configuration.
            var biome = PluginCodecs.Parse(PluginCodecs.Biome, "minecraft:plains", "later-plugin", """
                {
                  "has_precipitation": true,
                  "temperature": 0.5,
                  "downfall": 0.5,
                  "effects": { "water_color": "#3f76e4" },
                  "carvers": "featurefiles:caves",
                  "features": []
                }
                """u8, isConfiguredCarver: PluginFeatures.IsConfiguredCarver);
            var registry = PluginFeatures.BuildRegistry([file], [biome], [], NullLogger.Instance);
            Assert.Equal([file.Identifier], registry.Carvers.BiomeCarvers("minecraft:plains"));

            var terrain = CarverTerrain(() => PluginFeatures.CurrentCarvers);
            var activeAir = CarvedAir(terrain);
            Assert.Contains(file.Identifier, terrain.CarversAt(0, 0));

            var quiet = Parse(PluginFeatures.Carver, file.Identifier,
                EveryChunkCaves.Replace("\"probability\": 1", "\"probability\": 0"));
            Assert.True(PluginFeatures.Apply([quiet, modifier], [], [], new HashSet<string>(), NullLogger.Instance));
            Assert.True(CarvedAir(terrain) < activeAir);

            Assert.True(PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance));
            Assert.False(PluginFeatures.IsConfiguredCarver(file.Identifier));
            Assert.DoesNotContain(file.Identifier, terrain.CarversAt(0, 0));
        }
        finally
        {
            PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance);
        }
    }

    [Fact]
    public void RejectedReloadKeepsTheSuccessfulCarverAndUncarvableTagsBeforeFirstRead()
    {
        var modifier = Parse(PluginFeatures.BiomeModifier, "featurefiles:tagged_carvers", """
            { "biomes": "minecraft:plains", "add": { "carvers": "#featurefiles:carvers" } }
            """);
        FeatureFile[] cyclicFiles =
        [
            modifier,
            Parse(PluginFeatures.PlacedFeature, "featurefiles:cycle_a", NoOp),
            Parse(PluginFeatures.PlacedFeature, "featurefiles:cycle_b", NoOp)
        ];
        CodecFile[] cyclicBiomes =
        [
            CyclicBiome("minecraft:plains", "featurefiles:cycle_a", "featurefiles:cycle_b"),
            CyclicBiome("minecraft:forest", "featurefiles:cycle_b", "featurefiles:cycle_a")
        ];

        try
        {
            ApplyTags("minecraft:cave", "minecraft:stone");
            Assert.True(PluginFeatures.Apply([modifier], [], [], new HashSet<string>(), NullLogger.Instance));
            var previous = PluginFeatures.Current;

            // Do not read the native registry until after the next apply fails: it must retain the tags from
            // the successful apply even though the global tag registry already contains the rejected reload's.
            ApplyTags("minecraft:canyon", "minecraft:deepslate");
            Assert.False(PluginFeatures.Apply(cyclicFiles, cyclicBiomes, [],
                new HashSet<string> { "minecraft:overworld" }, NullLogger.Instance));
            Assert.Same(previous, PluginFeatures.Current);

            var retained = PluginFeatures.CurrentCarvers;
            Assert.Equal(("minecraft:cave", true, false), TagsOf(retained));

            // A subsequent successful apply takes the new tags, while the retained registry stays unchanged.
            Assert.True(PluginFeatures.Apply([modifier], [], [], new HashSet<string>(), NullLogger.Instance));
            var replacement = PluginFeatures.CurrentCarvers;
            Assert.NotSame(retained, replacement);
            Assert.Equal(("minecraft:canyon", false, true), TagsOf(replacement));
            Assert.Equal(("minecraft:cave", true, false), TagsOf(retained));
        }
        finally
        {
            PluginTags.Apply([], NullLogger.Instance);
            PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance);
        }

        static CodecFile CyclicBiome(string biome, string first, string second) => PluginCodecs.Parse(
            PluginCodecs.Biome, biome, "reload-plugin", Encoding.UTF8.GetBytes($$"""
                {
                  "has_precipitation": true,
                  "temperature": 0.5,
                  "downfall": 0.5,
                  "effects": { "water_color": "#3f76e4" },
                  "carvers": [],
                  "features": [["{{first}}", "{{second}}"]]
                }
                """), _ => true);

        static void ApplyTags(string carver, string block)
        {
            var carvers = PluginTags.Parse(PluginTags.ConfiguredCarver, "featurefiles:carvers", "reload-plugin",
                Encoding.UTF8.GetBytes($$"""{ "replace": true, "values": ["{{carver}}"] }"""));
            var uncarvable = PluginTags.Parse("block", "minecraft:uncarvable", "reload-plugin",
                Encoding.UTF8.GetBytes($$"""{ "replace": true, "values": ["{{block}}"] }"""));
            PluginTags.Apply([carvers, uncarvable], NullLogger.Instance);
        }

        static (string Carver, bool Stone, bool Deepslate) TagsOf(CarverRegistry carvers) =>
            (carvers.BiomeCarvers("minecraft:plains")[^1],
                carvers.Uncarvable!.Contains(BlocksRegistry.Stone.RegistryId),
                carvers.Uncarvable.Contains(BlocksRegistry.Deepslate.RegistryId));
    }

    // The air in chunk (0, 0) below Y 60 after the full 26.3 terrain status, including mask application.
    private static int CarvedAir(BiomeTerrain terrain)
    {
        var chunk = new Chunk(0, 0, terrain.MinY, terrain.Height);
        terrain.Generate(chunk);

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

    [Fact]
    public void LaterPluginsBiomesNamePlacedFeaturesOnlyThe26_3RegistryBuilds()
    {
        // In 26.3's schema, shared through the native adapter after a successful apply.
        var feature = Parse(PluginFeatures.Feature, "featurefiles:native_marker", """
            { "type": "minecraft:simple_block", "to_place": { "id": "minecraft:emerald_block" } }
            """);
        var placed = Parse(PluginFeatures.PlacedFeature, "featurefiles:native_marker", """
            { "feature": "featurefiles:native_marker" }
            """);

        try
        {
            Assert.True(PluginFeatures.Apply([feature, placed], [], [], new HashSet<string>(), NullLogger.Instance));
            Assert.NotNull(PluginFeatures.CurrentRegistry.FeatureSet!.FindPlaced(placed.Identifier));
            Assert.True(PluginFeatures.IsPlacedFeature(placed.Identifier));

            // A later plugin's biome is parsed before its files join the snapshot, and may name the placed feature.
            var biome = PluginCodecs.Parse(PluginCodecs.Biome, "minecraft:plains", "later-plugin", """
                {
                  "has_precipitation": true,
                  "temperature": 0.5,
                  "downfall": 0.5,
                  "effects": { "water_color": "#3f76e4" },
                  "carvers": [],
                  "features": [["featurefiles:native_marker"]]
                }
                """u8, isPlacedFeature: PluginFeatures.IsPlacedFeature);
            var registry = PluginFeatures.BuildRegistry([feature, placed], [biome], [], NullLogger.Instance);
            Assert.Contains(placed.Identifier, registry.BiomeSteps("minecraft:plains").SelectMany(step => step));

            Assert.True(PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance));
            Assert.False(PluginFeatures.IsPlacedFeature(placed.Identifier));
        }
        finally
        {
            PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance);
        }
    }

    private static FeatureFile Parse(string registry, string identifier, string json) =>
        PluginFeatures.Parse(registry, identifier, "test", Encoding.UTF8.GetBytes(json));

    private static string ProbeData(string path) =>
        File.ReadAllText(Path.Join(AppContext.BaseDirectory, "data", "probe", "worldgen", path));

    internal static bool HasDiamondBlocks(IChunk chunk) =>
        Enumerable.Range(0, 16 * 16 * 128).Any(i => chunk.GetBlock(i & 15, (i >> 8) - 64, (i >> 4) & 15)
            .Is(BlockIds.DiamondBlock));
}
