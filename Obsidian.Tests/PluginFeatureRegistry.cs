using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Features;
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

    internal static FeatureContext Context(WorldGenRegion region) => new()
    {
        Level = region,
        Generation = new WorldGenerationContext(-64, 384),
        Origin = new Vector(8, 0, 8),
        Random = new XoroshiroRandomSource(1)
    };

    internal static WorldGenRegion Region()
    {
        var chunk = new Chunk(0, 0, -64, 384);
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

    private static WorldgenData EmptyBiomes() => new(
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
