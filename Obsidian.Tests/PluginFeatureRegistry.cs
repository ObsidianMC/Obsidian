using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Trees;
using Obsidian.API.World.Generator;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.World.Structures;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Plugins.PluginProviders;
using Obsidian.Registries;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Features;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Pools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;
using PlacedFeature = Obsidian.WorldData.Generators.Features.PlacedFeature;

namespace Obsidian.Tests;

[Trait("Category", TestCategory.Worldgen)]
[Collection(nameof(PluginTagFiles))]
public sealed class PluginFeatureRegistry
{
    private const string Marker = """
        { "type": "minecraft:simple_block", "to_place": { "id": "minecraft:emerald_block" } }
        """;

    [Fact]
    public void RegisteredFeaturesKeepTransitiveOwnersAndRefuseUnloadingPlugins()
    {
        using var loaded = new LoadedTypes();
        var types = loaded.Register(
            ("probe:pillar", typeof(ConfiguredFeatureBase), typeof(ProbePillarFeature)),
            ("probe:at_height", typeof(PlacementModifierBase), typeof(ProbeAtHeight)),
            ("probe:failing", typeof(ConfiguredFeatureBase), typeof(FailingFeature)));
        var files = new[]
        {
            File(PluginFeatures.ConfiguredFeature, "probe:pillar", Probe("configured_feature/pillar.json")),
            File(PluginFeatures.PlacedFeature, "probe:pillar", Probe("placed_feature/pillar.json")),
            File(PluginFeatures.Feature, "probe:failing", """{ "type": "probe:failing" }"""),
            File(PluginFeatures.PlacedFeature, "probe:failing", """{ "feature": "probe:failing" }"""),
            File(PluginFeatures.Feature, "probe:outer", """
                { "type": "minecraft:random_boolean_selector",
                  "feature_true": "probe:pillar", "feature_false": "probe:pillar" }
                """),
            File(PluginFeatures.PlacedFeature, "probe:outer", """{ "feature": "probe:outer" }"""),
            File(PluginFeatures.Feature, "probe:healthy", Marker),
            File(PluginFeatures.Feature, "probe:root_pillar", """
                { "type": "probe:pillar", "state": "minecraft:emerald_block", "height": 2,
                  "replaceable": "minecraft:stone" }
                """),
            File(PluginFeatures.BiomeModifier, "probe:add", """
                { "biomes": "minecraft:plains", "add": { "features": { "underground_ores": "probe:outer" } } }
                """),
        };
        var logger = new PluginFeatureFiles.ListLogger();
        var registry = PluginFeatures.BuildRegistry(files, [], types, logger, EmptyBiomes());
        var set = registry.FeatureSet!;
        var outer = registry.Placed("probe:outer");

        Assert.Empty(logger.Messages);
        Assert.Equal(loaded.Plugin, Assert.Single(set.OwnersOf(set.FindPlaced("probe:outer")!)));
        Assert.Contains("probe:outer", new BiomeDecoration(registry, ["minecraft:plains"]).Steps.SelectMany(x => x));

        var region = Region();
        var context = new DecorationContext(registry, region, _ => "minecraft:plains", -64, 384);
        Assert.True(outer.Place(context, new XoroshiroRandomSource(1), new Vector(0, 0, 0)));
        Assert.Equal(8, Count(region, BlockIds.DiamondBlock));

        // Plant growth's existing interface places configured features on any IWorldGenLevel, without a region cast.
        var growth = new FeatureContext
        {
            Level = region,
            Generation = new WorldGenerationContext(-64, 384),
            Origin = new Vector(8, -4, 8),
            Random = new XoroshiroRandomSource(2)
        };
        Assert.True(set.PlaceConfigured("probe:pillar", growth));
        Assert.True(region.GetBlock(growth.Origin).Is(BlockIds.DiamondBlock));

        var root = Context(Region());
        Assert.True(set.PlaceConfigured("probe:root_pillar", root));
        Assert.True(root.Level.GetBlock(root.Origin).Is(BlockIds.EmeraldBlock));
        Assert.True(root.Level.GetBlock(root.Origin + Vector.Up).Is(BlockIds.EmeraldBlock));

        // The same failure boundary used by the live generator identifies only the failing plugin.
        var failing = registry.Placed("probe:failing");
        var failure = Assert.Throws<PluginGenerationException>(() =>
            failing.Place(context, growth.Random, growth.Origin));
        Assert.Equal(loaded.Plugin, Assert.Single(failure.Plugins));
        Assert.True(set.PlaceConfigured("probe:healthy", growth));
        Assert.True(region.GetBlock(growth.Origin).Is(BlockIds.EmeraldBlock));

        loaded.Plugin.Close();
        Assert.False(outer.Place(context, growth.Random, Vector.Zero));
        Assert.False(set.PlaceConfigured("probe:pillar", growth));
        Assert.True(set.PlaceConfigured("probe:healthy", growth));

        var closing = PluginFeatures.BuildRegistry(files, [], types, logger, EmptyBiomes());
        Assert.Null(closing.FeatureSet!.FindConfigured("probe:pillar"));

        var gone = PluginFeatures.BuildRegistry(files, [], [], logger, EmptyBiomes());
        Assert.Null(gone.FeatureSet!.FindConfigured("probe:pillar"));
        Assert.DoesNotContain("probe:outer", new BiomeDecoration(gone, ["minecraft:plains"]).Steps.SelectMany(x => x));
    }

    [Fact]
    public void VanillaFeaturesTakePluginsValueTypes()
    {
        using var loaded = new LoadedTypes();
        var types = loaded.Register(
            ("probe:count", typeof(IIntProvider), typeof(ProbeCount)),
            ("probe:failing_count", typeof(IIntProvider), typeof(FailingCount)),
            ("probe:height", typeof(IHeightProvider), typeof(ProbeHeight)),
            ("probe:air", typeof(IBlockPredicate), typeof(ProbeAir)),
            ("probe:state", typeof(IBlockStateProvider), typeof(ProbeState)),
            ("probe:stone", typeof(IRuleTest), typeof(ProbeStone)),
            ("probe:wind", typeof(IFloatProvider), typeof(ProbeWind)));
        var files = new[]
        {
            File(PluginFeatures.Feature, "probe:gold", """
                { "type": "minecraft:simple_block",
                  "to_place": { "type": "probe:state", "state": { "Name": "minecraft:gold_block" } } }
                """),
            File(PluginFeatures.PlacedFeature, "probe:gold", """
                { "feature": "probe:gold", "placement": [
                  { "type": "minecraft:count", "count": { "type": "probe:count", "value": 3 } },
                  { "type": "minecraft:height_range", "height": { "type": "probe:height", "y": 9 } },
                  { "type": "minecraft:block_predicate_filter", "predicate": { "type": "probe:air" } }
                ] }
                """),
            File(PluginFeatures.Feature, "probe:ore", """
                { "type": "minecraft:ore", "size": 12, "discard_chance_on_air_exposure": 0.0,
                  "targets": [{ "target": { "predicate_type": "probe:stone" },
                                "state": { "Name": "minecraft:diamond_block" } }] }
                """),
            File(PluginFeatures.Feature, "probe:dripstone", LargeDripstone),
            File(PluginFeatures.PlacedFeature, "probe:failing", """
                { "feature": "probe:healthy",
                  "placement": [{ "type": "minecraft:count", "count": { "type": "probe:failing_count" } }] }
                """),
            File(PluginFeatures.Feature, "probe:unknown", """
                { "type": "minecraft:simple_block", "to_place": { "type": "probe:unregistered" } }
                """),
            File(PluginFeatures.Feature, "probe:healthy", Marker)
        };
        var registry = PluginFeatures.BuildRegistry(files, [], types, NullLogger.Instance, EmptyBiomes());
        var set = registry.FeatureSet!;

        // Each entry using the plugin's values is the plugin's; a vanilla feature alone isn't.
        string[] owned = ["probe:gold", "probe:failing"];
        foreach (var id in owned)
            Assert.Equal(loaded.Plugin, Assert.Single(set.OwnersOf(set.FindPlaced(id)!)));

        Assert.Equal(loaded.Plugin, Assert.Single(set.OwnersOf(set.FindConfigured("probe:dripstone")!)));
        Assert.Empty(set.OwnersOf(set.FindConfigured("probe:healthy")!));
        Assert.Null(set.FindConfigured("probe:unknown"));

        // The count, height, filter and provider are the plugin's: its block at its height.
        var region = Region();
        var context = new DecorationContext(registry, region, _ => "minecraft:plains", -64, 384);
        var gold = registry.Placed("probe:gold");
        Assert.True(gold.Place(context, new XoroshiroRandomSource(1), new Vector(3, 0, 3)));
        Assert.True(region.GetBlock(new Vector(3, 9, 3)).Is(BlockIds.GoldBlock));

        // The ore replaces what the plugin's rule test accepts.
        Assert.True(set.PlaceConfigured("probe:ore", Context(region)));
        Assert.True(Count(region, BlockIds.DiamondBlock) > 0);

        // A plugin's value failing fails its entry's placement, naming the plugin.
        var failing = registry.Placed("probe:failing");
        var failure = Assert.Throws<PluginGenerationException>(() =>
            failing.Place(context, new XoroshiroRandomSource(1), Vector.Zero));
        Assert.Equal(loaded.Plugin, Assert.Single(failure.Plugins));

        // An unloading plugin's values place nothing; vanilla's go on.
        loaded.Plugin.Close();
        Assert.False(gold.Place(context, new XoroshiroRandomSource(1), new Vector(5, 0, 5)));
        Assert.True(set.PlaceConfigured("probe:healthy", Context(region)));
    }

    [Fact]
    public void VanillaTreesTakePluginsTreeParts()
    {
        using var loaded = new LoadedTypes();
        var types = loaded.Register(
            ("probe:pole", typeof(TrunkPlacer), typeof(PoleTrunkPlacer)),
            ("probe:cube", typeof(FoliagePlacer), typeof(WorldgenFeatureReading.CubeFoliagePlacer)),
            ("probe:column", typeof(FeatureSize), typeof(WorldgenFeatureReading.ColumnFeatureSize)),
            ("probe:stilts", typeof(RootPlacer), typeof(StiltRootPlacer)),
            ("probe:cap", typeof(TreeDecorator), typeof(WorldgenFeatureReading.CapDecorator)));
        var files = new[] { File(PluginFeatures.Feature, "probe:tree", PluginTree) };
        var registry = PluginFeatures.BuildRegistry(files, [], types, NullLogger.Instance, EmptyBiomes());
        var set = registry.FeatureSet!;

        Assert.Equal(loaded.Plugin, Assert.Single(set.OwnersOf(set.FindConfigured("probe:tree")!)));

        // Stilts up to a trunk raised two blocks, a pole of four logs, a cube of leaves and a cap on the pole.
        var region = Region();
        var origin = new Vector(8, 8, 8);
        var growth = Context(region, origin);
        Assert.True(set.PlaceConfigured("probe:tree", growth));

        Assert.True(region.GetBlock(origin).Is(BlockIds.MangroveRoots));
        Assert.True(region.GetBlock(origin + (0, 1, 0)).Is(BlockIds.MangroveRoots));
        Assert.True(region.GetBlock(origin + (0, 2, 0)).Is(BlockIds.OakLog));
        Assert.True(region.GetBlock(origin + (0, 5, 0)).Is(BlockIds.OakLog));
        Assert.True(region.GetBlock(origin + (1, 6, 1)).Is(BlockIds.OakLeaves));
        Assert.True(region.GetBlock(origin + (0, 6, 0)).Is(BlockIds.GoldBlock));

        loaded.Plugin.Close();
        Assert.False(set.PlaceConfigured("probe:tree", Context(Region(), origin)));
    }

    [Fact]
    public void NestedGenerationFailuresAndStopsKeepTheirOriginalIdentity()
    {
        using var loaded = new LoadedTypes();
        var failure = new PluginGenerationException(
            [loaded.Plugin], "An inner feature failed.", new InvalidOperationException());
        var stopped = new GeneratorStoppedException("The dimension stopped.");

        // Match the dimension boundary: an already attributed failure must retain its original owners, and a
        // stopped dimension must not be converted into a plugin failure by an outer feature or growth adapter.
        Assert.Same(failure, Assert.Throws<PluginGenerationException>(() =>
            WorldgenFeatureSet.Place([loaded.Plugin], "outer feature", () => throw failure)));
        Assert.Same(stopped, Assert.Throws<GeneratorStoppedException>(() =>
            WorldgenFeatureSet.Place([loaded.Plugin], "outer feature", () => throw stopped)));
    }

    [Fact]
    public void InvalidFilesAndCyclesLeaveOtherPluginsFeaturesAvailable()
    {
        var files = new[]
        {
            File(PluginFeatures.Feature, "probe:missing", """{ "type": "probe:unregistered" }"""),
            File(PluginFeatures.PlacedFeature, "probe:missing", """{ "feature": "probe:missing" }"""),
            File(PluginFeatures.Feature, "probe:cycle", """
                { "type": "minecraft:random_boolean_selector",
                  "feature_true": { "feature": "probe:cycle" },
                  "feature_false": { "feature": "probe:cycle" } }
                """),
            File(PluginFeatures.Feature, "probe:healthy", Marker)
        };
        var logger = new PluginFeatureFiles.ListLogger();
        var registry = PluginFeatures.BuildRegistry(files, [], [], logger, EmptyBiomes());
        var set = registry.FeatureSet!;

        Assert.Null(set.FindConfigured("probe:missing"));
        Assert.Null(set.FindPlaced("probe:missing"));
        Assert.Null(set.FindConfigured("probe:cycle"));
        Assert.True(set.PlaceConfigured("probe:healthy", Context(Region())));
    }

    [Fact]
    public void BothFeatureFoldersShareLoadOrder()
    {
        var old = File(PluginFeatures.ConfiguredFeature, "probe:marker", """
            { "type": "minecraft:simple_block", "config": {
              "to_place": { "type": "minecraft:simple_state_provider", "state": { "Name": "minecraft:diamond_block" } }
            } }
            """);
        var native = File(PluginFeatures.Feature, "probe:marker", Marker);
        var cases = new (FeatureFile[] Files, int Block)[]
        {
            ([old, native], BlockIds.EmeraldBlock),
            ([native, old], BlockIds.DiamondBlock)
        };

        foreach (var (files, block) in cases)
        {
            var registry = PluginFeatures.BuildRegistry(files, [], [], NullLogger.Instance, EmptyBiomes());
            var context = Context(Region());
            Assert.True(registry.FeatureSet!.PlaceConfigured("probe:marker", context));
            Assert.True(context.Level.GetBlock(context.Origin).Is(block));
        }
    }

    [Fact]
    public void StructuresKeepOwnersOfRegisteredTypesAndFeaturePoolDependencies()
    {
        using var loaded = new LoadedTypes();
        using var processors = new LoadedTypes();
        var structureType = typeof(PluginStructureTypes).GetNestedType(
            "SpireType", System.Reflection.BindingFlags.NonPublic)!;
        var types = loaded.Register(
            ("structuretest:spire", typeof(CustomStructure), structureType),
            ("probe:pillar", typeof(ConfiguredFeatureBase), typeof(ProbePillarFeature)));
        types.AddRange(processors.Register(("probe:processor", typeof(StructureProcessor), typeof(ProbeProcessor))));
        var features = PluginFeatures.BuildRegistry(
            [
                File(PluginFeatures.ConfiguredFeature, "probe:pillar", Probe("configured_feature/pillar.json")),
                File(PluginFeatures.PlacedFeature, "probe:pillar", """{ "feature": "probe:pillar" }"""),
                File(PluginFeatures.ProcessorList, "minecraft:mossify_10_percent", """
                    { "processors": [{ "processor_type": "probe:processor" }] }
                    """)
            ], [], types, NullLogger.Instance, EmptyBiomes());
        var pool = PluginStructures.Parse(
            PluginStructures.TemplatePool,
            "probe:pool",
            "plugin",
            """
            { "fallback": "minecraft:empty", "elements": [{ "weight": 1, "element": {
              "element_type": "minecraft:feature_pool_element", "feature": "probe:pillar", "projection": "rigid"
            } }] }
            """u8);
        var bad = PluginStructures.Parse(
            PluginStructures.Structure, "probe:broken", "plugin", """{ "type": "probe:missing" }"""u8);
        var logger = new PluginFeatureFiles.ListLogger();
        var structures = StructureData.Read(
            features, [.. PluginStructureTypes.SpireFiles(), pool, bad], [], types, logger);

        var spire = structures.Structures[PluginStructureTypes.Spire];
        Assert.Equal(loaded.Plugin, Assert.Single(structures.Owners[spire]));
        Assert.False(structures.Structures.ContainsKey("probe:broken"));

        // Jigsaw blocks can name any pool, including a plugin's from an overridden template.
        var village = structures.Structures["minecraft:village_plains"];
        Assert.Equal(2, structures.Owners[village].Length);
        Assert.Contains(loaded.Plugin, structures.Owners[village]);
        Assert.Contains(processors.Plugin, structures.Owners[village]);
        var element = Assert.IsType<FeaturePoolElement>(Assert.Single(structures.Pools["probe:pool"].Elements).Element);
        Assert.True(element.UnnamedJigsaw);

        var gone = StructureData.Read(
            PluginFeatures.BuildRegistry([], [], [], NullLogger.Instance, EmptyBiomes()),
            [], [], [], NullLogger.Instance);
        Assert.True(gone.Version > structures.Version);
        Assert.Empty(gone.Owners);
        Assert.False(gone.Structures.ContainsKey(PluginStructureTypes.Spire));
        Assert.Null(gone.Pool("probe:pool"));
    }

    [Fact]
    public void ApplyReplacesThe26_3SnapshotAlongsideLegacyFeatures()
    {
        var file = File(PluginFeatures.ConfiguredFeature, "probe:marker", """
            { "type": "minecraft:no_op", "config": {} }
            """ );
        try
        {
            Assert.True(PluginFeatures.Apply([file], [], [], new HashSet<string>(), NullLogger.Instance));
            var before = PluginFeatures.CurrentRegistry;
            Assert.NotNull(before.FeatureSet!.FindConfigured("probe:marker"));
            Assert.NotNull(PluginFeatures.Current.FindConfigured("probe:marker"));

            Assert.True(PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance));
            Assert.NotSame(before, PluginFeatures.CurrentRegistry);
            Assert.Null(PluginFeatures.CurrentRegistry.FeatureSet!.FindConfigured("probe:marker"));
            Assert.Null(PluginFeatures.Current.FindConfigured("probe:marker"));
        }
        finally
        {
            PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance);
        }
    }

    internal static FeatureContext Context(WorldGenRegion region) => Context(region, new Vector(8, 0, 8));

    internal static FeatureContext Context(WorldGenRegion region, Vector origin) => new()
    {
        Level = region,
        Generation = new WorldGenerationContext(-64, 384),
        Origin = origin,
        Random = new XoroshiroRandomSource(1)
    };

    internal static WorldGenRegion Region() => Region(out _);

    internal static WorldGenRegion Region(out Chunk chunk)
    {
        chunk = new Chunk(0, 0, -64, 384);
        for (var y = -4; y < 8; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    chunk.SetBlock(x, y, z, BlocksRegistry.Stone);
            }
        }

        return new WorldGenRegion(
            new Dictionary<(int, int), IChunk> { [(0, 0)] = chunk },
            0, 0, 1, -64, 384, 63,
            new FixedBiomeSource(CodecRegistry.Biomes.Plains), new XoroshiroRandomSource(1), false);
    }

    private static int Count(WorldGenRegion region, int block) => Enumerable.Range(0, 16 * 16 * 12)
        .Count(i => region.GetBlock(new Vector(i & 15, (i >> 8) - 4, (i >> 4) & 15)).Is(block));

    private static FeatureFile File(string registry, string id, string json) =>
        PluginFeatures.Parse(registry, id, "plugin", Encoding.UTF8.GetBytes(json));

    private static string Probe(string path) => System.IO.File.ReadAllText(
        Path.Join(AppContext.BaseDirectory, "data", "probe", "worldgen", path));

    internal static WorldgenData EmptyBiomes() => new(
        (registry, id) => registry == "biome"
            ? new MemoryStream("""{ "features": [] }"""u8.ToArray())
            : new MemoryStream(Encoding.UTF8.GetBytes(WorldgenData.Vanilla.Read(registry, id).GetRawText())),
        registry => registry == "biome" ? ["minecraft:plains"] : WorldgenData.Vanilla.Ids(registry));

    public sealed class FailingFeature : ConfiguredFeatureBase
    {
        public override string Type => "probe:failing";

        public override bool Place(FeatureContext context) => throw new InvalidOperationException("Plugin failure.");
    }

    public sealed class ProbeProcessor : StructureProcessor;

    // 26.3's large dripstone, with the plugin's wind speed.
    private const string LargeDripstone = """
        { "type": "minecraft:large_dripstone",
          "replaceable_blocks": "#minecraft:dripstone_replaceable_blocks",
          "column_radius": { "type": "minecraft:uniform", "min_inclusive": 3, "max_inclusive": 19 },
          "height_scale": { "type": "minecraft:uniform", "min_inclusive": 0.4, "max_exclusive": 2.0 },
          "max_column_radius_to_cave_height_ratio": 0.33,
          "stalactite_bluntness": { "type": "minecraft:uniform", "min_inclusive": 0.3, "max_exclusive": 0.9 },
          "stalagmite_bluntness": { "type": "minecraft:uniform", "min_inclusive": 0.4, "max_exclusive": 1.0 },
          "wind_speed": { "type": "probe:wind" },
          "min_radius_for_wind": 4,
          "min_bluntness_for_wind": 0.6 }
        """;

    // A 26.3 tree of only the plugin's parts, with vanilla's providers.
    private const string PluginTree = """
        { "type": "minecraft:tree",
          "trunk_provider": { "type": "minecraft:simple_state_provider", "state": { "Name": "minecraft:oak_log" } },
          "foliage_provider": {
            "type": "minecraft:simple_state_provider", "state": { "Name": "minecraft:oak_leaves" }
          },
          "below_trunk_provider": "minecraft:soil_beneath_tree",
          "trunk_placer": { "type": "probe:pole", "base_height": 4, "height_rand_a": 0, "height_rand_b": 0 },
          "foliage_placer": { "type": "probe:cube", "radius": 1, "offset": 0, "height": 2 },
          "minimum_size": { "type": "probe:column" },
          "root_placer": {
            "type": "probe:stilts",
            "trunk_offset_y": 2,
            "root_provider": {
              "type": "minecraft:simple_state_provider", "state": { "Name": "minecraft:mangrove_roots" }
            }
          },
          "decorators": [{ "type": "probe:cap", "state": { "Name": "minecraft:gold_block" } }] }
        """;

    // A plugin's int provider: always its value.
    public sealed class ProbeCount : IIntProvider
    {
        public string Type => "probe:count";

        public required int Value { get; init; }

        public int MinValue => this.Value;

        public int MaxValue => this.Value;

        public int Sample(IRandomSource random) => this.Value;
    }

    public sealed class FailingCount : IIntProvider
    {
        public string Type => "probe:failing_count";

        public int MinValue => 0;

        public int MaxValue => 0;

        public int Sample(IRandomSource random) => throw new InvalidOperationException("Plugin failure.");
    }

    // A plugin's height provider: always its Y.
    public sealed class ProbeHeight : IHeightProvider
    {
        public string Type => "probe:height";

        public required int Y { get; init; }

        public int Sample(IRandomSource random, WorldGenerationContext context) => this.Y;
    }

    public sealed class ProbeAir : IBlockPredicate
    {
        public string Type => "probe:air";

        public bool Test(IWorldGenLevel level, Vector position) => level.GetBlock(position).IsAir;
    }

    public sealed class ProbeState : IBlockStateProvider
    {
        public string Type => "probe:state";

        public required SimpleBlockState State { get; init; }

        public IBlock GetState(IRandomSource random, Vector position) => this.State.ToBlock();
    }

    public sealed class ProbeStone : IRuleTest
    {
        public string Type => "probe:stone";

        public bool Test(IBlock block, IRandomSource random) => block.Is(BlockIds.Stone);
    }

    public sealed class ProbeWind : IFloatProvider
    {
        public string Type => "probe:wind";

        public float MinValue => 0.0f;

        public float MaxValue => 0.0f;

        public float Sample(IRandomSource random) => 0.0f;
    }

    // A plugin's trunk placer: a column of logs, with its foliage on top.
    public sealed class PoleTrunkPlacer : TrunkPlacer
    {
        public override List<FoliageAttachment> PlaceTrunk(TreeContext tree, int freeTreeHeight, Vector origin)
        {
            for (var y = 0; y < freeTreeHeight; y++)
                this.PlaceLog(tree, origin + (0, y, 0));

            return [new FoliageAttachment(origin + (0, freeTreeHeight, 0), 0, false)];
        }
    }

    // A plugin's root placer: a column of roots from the origin up to the trunk.
    public sealed class StiltRootPlacer : RootPlacer
    {
        public override bool PlaceRoots(TreeContext tree, Vector origin, Vector trunkOrigin)
        {
            for (var position = origin; position.Y < trunkOrigin.Y; position += Vector.Up)
                this.PlaceRoot(tree, position);

            return true;
        }
    }

    private sealed class LoadedTypes : IDisposable
    {
        private readonly PluginLoadContext context = new("26.3-feature-probe");
        private readonly System.Reflection.Assembly assembly;

        public PluginContainer Plugin { get; } = new()
        {
            Info = new PluginInfo("featureprobe"),
            Archive = PluginArchive.Open("probe.obby", PluginArchives.Zip(new Dictionary<string, byte[]>
            {
                [PluginMetadata.FileName] = PluginArchives.Metadata("featureprobe", "1.0.0", "Probe")
            }))
        };

        public LoadedTypes()
        {
            this.context.Container = this.Plugin;
            var bytes = System.IO.File.ReadAllBytes(typeof(ProbePillarFeature).Assembly.Location);
            this.assembly = this.context.LoadAssembly(bytes)!;
        }

        public List<WorldgenTypeRegistration> Register(params (string Id, Type Base, Type Type)[] types)
        {
            List<WorldgenTypeRegistration> result = [];
            foreach (var (id, baseType, type) in types)
            {
                result.Add(WorldgenTypeRegistration.Create(
                    result, id, baseType, this.assembly.GetType(type.FullName!, throwOnError: true)!));
            }

            return result;
        }

        public void Dispose()
        {
            this.Plugin.Close();
            this.context.Container = null;
            this.context.Unload();
        }
    }
}
