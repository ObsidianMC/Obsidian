using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Registries;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator;
using Obsidian.Nbt;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Features;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
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

    private const string NoOp = """{ "feature": { "type": "minecraft:no_op", "config": {} }, "placement": [] }""";

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

    [Trait("Category", TestCategory.Worldgen)]
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

        // A vanilla dimension keeps its type, and what Obsidian can't generate yet is rejected, saying so.
        Assert.Throws<InvalidDataException>(() => Parse("minecraft:the_nether", FixedStem));
        var flat = Assert.Throws<InvalidDataException>(() => Parse(
            "stemfiles:flat",
            """
            { "type": "overworld", "generator": { "type": "flat", "settings": { "layers": [], "lakes": true } } }
            """));
        Assert.Contains("lakes", flat.Message);
        Assert.Throws<InvalidDataException>(() => Parse(
            "stemfiles:other",
            FixedStem.Replace("\"settings\": \"minecraft:overworld\"", "\"settings\": \"stemfiles:settings\"")));

        var multiNoise = Parse("stemfiles:meadow", MultiNoiseStem).Dimension.Noise();
        Assert.Equal(["stemfiles:glade", "minecraft:plains"], multiNoise.PossibleBiomes.Select(biome => biome.Name));

        var dimension = Parse("stemfiles:glade_world", FixedStem).Dimension.Noise();
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

    [Trait("Category", TestCategory.Worldgen)]
    [Fact(DisplayName = "A dimension generates with plugins' noise settings and types, and stops when they unload")]
    public async Task GeneratesWithPluginNoiseSettingsAndTypes()
    {
        // The types a plugin registers, here from the test's own load context.
        List<WorldgenTypeRegistration> types = [];
        types.Add(WorldgenTypeRegistration.Create(
            types,
            "probe:shelf",
            typeof(IDensityFunction),
            typeof(ShelfFunction)));
        types.Add(WorldgenTypeRegistration.Create(
            types,
            "probe:top",
            typeof(ISurfaceRule),
            typeof(TopRule)));
        types.Add(WorldgenTypeRegistration.Create(
            types,
            "probe:halves",
            typeof(BiomeSourceBase),
            typeof(HalvesSource)));
        types.Add(WorldgenTypeRegistration.Create(
            types,
            "probe:failing",
            typeof(IDensityFunction),
            typeof(FailingFunction)));

        // A density function with functions in it has to map them, or they'd stay unseeded without any error.
        Assert.Throws<ArgumentException>(() => WorldgenTypeRegistration.Create(
            types,
            "probe:unmapped",
            typeof(IDensityFunction),
            typeof(UnmappedFunction)));

        var logger = new PluginFeatureFiles.ListLogger();
        var noise = PluginNoise.Build(
            [
                PluginNoise.Parse(PluginNoise.NoiseSettings, "probe:shelf_world", "test", Bytes(ShelfWorld)),
                PluginNoise.Parse(PluginNoise.DensityFunction, "probe:ground", "test", Bytes(Ground)),
                PluginNoise.Parse(PluginNoise.DensityFunction, "probe:loop", "test", Bytes("\"probe:loop\"")),
                PluginNoise.Parse(PluginNoise.DensityFunction, "probe:stuck", "test", Bytes(Stuck)),
                PluginNoise.Parse(PluginNoise.NoiseSettings, "probe:failing_world", "test", Bytes(FailingWorld)),
                PluginNoise.Parse(PluginNoise.Noise, "probe:bumps", "test", Bytes(Bumps))
            ],
            types,
            logger);

        // A file referring back to itself, or with a value vanilla's codec refuses, is left out; the others load.
        Assert.Collection(
            logger.Messages,
            message => Assert.Contains("probe:loop refers back to itself", message),
            message => Assert.Contains("cell_height: 0 isn't positive", message));

        var settings = noise.NoiseSettings["probe:shelf_world"];
        var shelf = Assert.IsType<ShelfFunction>(settings.NoiseRouter.FinalDensity);
        Assert.Same(noise.DensityFunctions["probe:ground"], shelf.Argument);

        var stem = FixedStem
            .Replace("\"settings\": \"minecraft:overworld\"", "\"settings\": \"probe:shelf_world\"")
            .Replace(
                """{ "type": "minecraft:fixed", "biome": "stemfiles:glade" }""",
                """{ "type": "probe:halves", "west": "minecraft:desert", "east": "minecraft:plains" }""");
        var read = PluginDimensions.Parse("stemfiles:shelf", "test", Bytes(stem), noise).Dimension.Noise();

        // The types are the test's, not a plugin's, so the plugin whose code the generator runs is given here.
        var plugin = new PluginContainer
        {
            Info = new PluginInfo("probe"),
            Archive = PluginArchive.Open(
                "probe.obby",
                PluginArchives.Zip(new Dictionary<string, byte[]>
                {
                    [PluginMetadata.FileName] = PluginArchives.Metadata("probe", "1.0.0", "Probe")
                }))
        };

        var dimension = new MojangDimension(
            read.Identifier,
            "minecraft:overworld",
            read.NoiseSettings,
            read.Settings,
            read.CreateBiomeSource,
            () => read.PossibleBiomes,
            [plugin]);

        Assert.Equal(["minecraft:desert", "minecraft:plains"], dimension.PossibleBiomes.Select(biome => biome.Name));

        // The plugin's function shapes a floor at y 40, which its rule tops with gold, in the source's biomes.
        var builder = new ChunkBuilder(dimension, 1234, generateStructures: false, () => WorldgenFeatureSet.Vanilla);
        var west = new Chunk(-1, 0);

        builder.PopulateBiomes(west);
        builder.Generate3DTerrain(west);
        builder.ApplySurfaceRules(west);

        Assert.Equal("minecraft:desert", west.GetBiome(0, 39, 0).Name);
        Assert.True(west.GetBlock(3, 39, 5).Is(BlockIds.GoldBlock));
        Assert.True(west.GetBlock(3, 38, 5).Is(BlockIds.Stone));
        Assert.True(west.GetBlock(3, 40, 5).IsAir);

        // The same settings defined inline generate the same, and the plugin entries they name count as changes.
        var inlineFile = PluginDimensions.Parse(
            "stemfiles:inline",
            "test",
            Bytes(stem.Replace("\"probe:shelf_world\"", ShelfWorld)),
            noise);
        Assert.Null(inlineFile.Dimension.Noise().NoiseSettings);
        Assert.Contains($"{PluginNoise.DensityFunction} probe:ground", inlineFile.Json);

        var inlineBuilder = new ChunkBuilder(
            inlineFile.Dimension.Noise(),
            1234,
            generateStructures: false,
            () => WorldgenFeatureSet.Vanilla);
        var inlineWest = new Chunk(-1, 0);

        inlineBuilder.PopulateBiomes(inlineWest);
        inlineBuilder.Generate3DTerrain(inlineWest);
        inlineBuilder.ApplySurfaceRules(inlineWest);

        Assert.True(inlineWest.GetBlock(3, 39, 5).Is(BlockIds.GoldBlock));
        Assert.True(inlineWest.GetBlock(3, 40, 5).IsAir);

        // A plugin's code failing stops its level, rather than chunk management, and the chunk isn't generated.
        var failing = new MojangDimension(
            "stemfiles:failing",
            "minecraft:overworld",
            "probe:failing_world",
            noise.NoiseSettings["probe:failing_world"],
            read.CreateBiomeSource,
            () => read.PossibleBiomes,
            [plugin]);
        await using (var failingLevel = CreateLevel(this.CreateWorld(), failing))
        {
            var failingGenerator = (LevelStemGenerator)failingLevel.Generator;

            // The noise failed partway, so the chunk is rewound to replay its steps from scratch.
            Assert.True((await failingGenerator.GenerateChunkAsync(0, 0)).ChunkStatus < ChunkGenStage.biomes);
            Assert.Null(failingGenerator.Builder);
        }

        // A plugin's feature failing stops the level too, but its chunk counts as decorated: what was placed stays,
        // and isn't placed again after a restart.
        var failingFeature = new PlacedFeature
        {
            Identifier = "probe:failing",
            Feature = new FailingFeature()
        };
        var features = new WorldgenFeatureSet(
            FrozenDictionary<string, ConfiguredFeatureBase>.Empty,
            FrozenDictionary<string, PlacedFeature>.Empty,
            BiomeFeatures.All.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<IReadOnlyList<PlacedFeature>>)[[failingFeature], .. entry.Value.Skip(1)]),
            new Dictionary<PlacedFeature, ImmutableArray<PluginContainer>> { [failingFeature] = [plugin] }
                .ToFrozenDictionary());
        var decoratingGenerator = new LevelStemGenerator(dimension) { Features = () => features };
        await using (CreateLevel(this.CreateWorld(), dimension, generator: decoratingGenerator))
        {
            var decorated = await decoratingGenerator.GenerateChunkAsync(0, 0, stage: ChunkGenStage.features);

            Assert.Equal(ChunkGenStage.features, decorated.ChunkStatus);
            Assert.True(decorated.GetBlock(1, 100, 1).Is(BlockIds.DiamondBlock));
            Assert.Null(decoratingGenerator.Builder);
        }

        // Unloading: the generator holds the plugin while a level is created with it, then refuses to generate.
        var world = this.CreateWorld();
        await using var level = CreateLevel(world, dimension);
        var generator = (LevelStemGenerator)level.Generator;

        plugin.Close();

        Assert.Throws<GeneratorStoppedException>(() => builder.PopulateBiomes(new Chunk(2, 0)));

        var refused = await generator.GenerateChunkAsync(4, 4);
        Assert.False(refused.IsGenerated);

        // Stopped, it lets go of the plugin's objects, and generates nothing.
        generator.Stop();

        Assert.Null(generator.Builder);
        Assert.True(dimension.IsReleased);
        Assert.Equal(["minecraft:desert", "minecraft:plains"], dimension.PossibleBiomes.Select(biome => biome.Name));
        Assert.False((await generator.GenerateChunkAsync(5, 5)).IsGenerated);

        // Once the plugin loads again, the dimension read again from its stem resumes the level from its next chunk.
        var reloadedPlugin = new PluginContainer
        {
            Info = plugin.Info,
            Archive = plugin.Archive
        };
        var rebuilt = new MojangDimension(
            read.Identifier,
            "minecraft:overworld",
            read.NoiseSettings,
            read.Settings,
            read.CreateBiomeSource,
            () => read.PossibleBiomes,
            [reloadedPlugin]);

        Assert.True(generator.Resume(rebuilt));
        Assert.Same(rebuilt, generator.Dimension);

        var resumed = await generator.GenerateChunkAsync(6, 6, stage: ChunkGenStage.carvers);
        Assert.True(resumed.ChunkStatus >= ChunkGenStage.surface);
        Assert.True(resumed.GetBlock(3, 39, 5).Is(BlockIds.GoldBlock));
    }

    private const string Bumps = """{ "firstOctave": -4, "amplitudes": [1.0, 0.5] }""";

    // Vanilla's codec wants a positive cell height: with none, the search would never end.
    private const string Stuck = """
        { "type": "find_top_surface", "density": 0, "upper_bound": 0, "lower_bound": 0, "cell_height": 0 }
        """;

    private static readonly string FailingWorld = ShelfWorld.Replace(
        """{ "type": "probe:shelf", "y": 40, "argument": "probe:ground" }""",
        """{ "type": "probe:failing" }""");

    // Solid, from a plugin noise, at least 2.5 everywhere.
    private const string Ground = """
        {
          "type": "minecraft:add",
          "argument1": 4.0,
          "argument2": { "type": "minecraft:noise", "noise": "probe:bumps", "xz_scale": 0.25, "y_scale": 0.0 }
        }
        """;

    private const string ShelfWorld = """
        {
          "sea_level": -64,
          "disable_mob_generation": false,
          "aquifers_enabled": false,
          "ore_veins_enabled": false,
          "legacy_random_source": false,
          "default_block": { "Name": "minecraft:stone" },
          "default_fluid": { "Name": "minecraft:water", "Properties": { "level": "0" } },
          "noise": { "min_y": -64, "height": 384, "size_horizontal": 1, "size_vertical": 2 },
          "noise_router": {
            "barrier": 0,
            "fluid_level_floodedness": 0,
            "fluid_level_spread": 0,
            "lava": 0,
            "temperature": 0,
            "vegetation": 0,
            "continents": 0,
            "erosion": 0,
            "depth": 0,
            "ridges": 0,
            "preliminary_surface_level": 0,
            "final_density": { "type": "probe:shelf", "y": 40, "argument": "probe:ground" },
            "vein_toggle": 0,
            "vein_ridged": 0,
            "vein_gap": 0
          },
          "spawn_target": [],
          "surface_rule": {
            "type": "minecraft:sequence",
            "sequence": [{ "type": "probe:top", "block": { "Name": "minecraft:gold_block" }, "depth": 1 }]
          }
        }
        """;

    // A plugin's density function: its argument below a height, and air above.
    public sealed class ShelfFunction : IDensityFunction
    {
        public string Type => "probe:shelf";

        public required IDensityFunction Argument { get; init; }

        public required int Y { get; init; }

        public double MinValue => Math.Min(-1.0, this.Argument.MinValue);

        public double MaxValue => Math.Max(-1.0, this.Argument.MaxValue);

        public double GetValue(double x, double y, double z) => y < this.Y ? this.Argument.GetValue(x, y, z) : -1.0;

        public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new ShelfFunction
        {
            Argument = visitor.Map(this.Argument),
            Y = this.Y
        });
    }

    // A plugin's feature whose code fails after placing a block.
    public sealed class FailingFeature : ConfiguredFeatureBase
    {
        public override string Type => "probe:failing";

        public override bool Place(FeatureContext context)
        {
            context.Level.SetBlock(new Vector(1, 100, 1), BlocksRegistry.DiamondBlock);
            throw new InvalidOperationException("probe failed");
        }
    }

    // A plugin's density function whose code fails.
    public sealed class FailingFunction : IDensityFunction
    {
        public string Type => "probe:failing";

        public double MinValue => -1.0;

        public double MaxValue => 1.0;

        public double GetValue(double x, double y, double z) => throw new InvalidOperationException("probe failed");
    }

    // A density function that doesn't map the function in it.
    public sealed class UnmappedFunction : IDensityFunction
    {
        public string Type => "probe:unmapped";

        public required IDensityFunction Argument { get; init; }

        public double MinValue => this.Argument.MinValue;

        public double MaxValue => this.Argument.MaxValue;

        public double GetValue(double x, double y, double z) => this.Argument.GetValue(x, y, z);
    }

    // A plugin's surface rule: a block for the top blocks of the default block.
    public sealed class TopRule : ICustomSurfaceRule
    {
        public string Type => "probe:top";

        public required SimpleBlockState Block { get; init; }

        public required int Depth { get; init; }

        public IBlock? Apply(ISurfaceContext context) =>
            context.StoneDepthAbove <= this.Depth ? this.Block.ToBlock() : null;
    }

    // A plugin's biome source: one biome west of x 0, another east.
    public sealed class HalvesSource : BiomeSourceBase
    {
        public override string Type => "probe:halves";

        public required string West { get; init; }

        public required string East { get; init; }

        public override IReadOnlyList<string> PossibleBiomes => [this.West, this.East];

        public override string GetBiome(int quartX, int quartY, int quartZ, IClimateSampler climate) =>
            quartX < 0 ? this.West : this.East;
    }

    [Fact(DisplayName = "A file path giving an identifier with an empty, . or .. segment or namespace is refused")]
    public void RefusesPathsOutOfTheirFolder()
    {
        // data/<ns>/dimension//world.json would be <ns>:/world, whose folder would leave the world's folder.
        var (_, identifier) = Assert.Single(PluginDimensions.FindFiles(["data/stemfiles/dimension//world.json"]));
        Assert.Equal("stemfiles:/world", identifier);

        // data/./dimension/ruby/world.json would be .:ruby/world, sharing ruby:world's folder.
        var (_, dotted) = Assert.Single(PluginDimensions.FindFiles(["data/./dimension/ruby/world.json"]));
        Assert.Equal(".:ruby/world", dotted);

        string[] identifiers =
        [
            identifier,
            dotted,
            "..:world",
            "ruby.:world",
            "stemfiles:world/",
            "stemfiles:a//b",
            "stemfiles:../world",
            "stemfiles:a/./b"
        ];

        foreach (var bad in identifiers)
        {
            var refused = Assert.Throws<InvalidDataException>(() => Parse(bad, FixedStem));
            Assert.Contains("isn't a valid identifier", refused.Message);
        }
    }

    [Fact(DisplayName = "Reverting one dimension's modifiers re-checks the dimensions sharing its biomes")]
    public void RevertsUntilEveryDimensionIsOrdered()
    {
        var logger = NullLogger.Instance;
        bool isFeature(string id) => id is "stemfiles:x" or "stemfiles:y" or "stemfiles:z";

        CodecFile[] biomes =
        [
            Biome("stemfiles:roll_a", """["stemfiles:x", "stemfiles:y"]""", isFeature),
            Biome("stemfiles:roll_b", """["stemfiles:y"]""", isFeature),
            Biome("stemfiles:roll_c", """["stemfiles:z"]""", isFeature)
        ];

        PluginCodecs.Apply(biomes, logger);

        // With every modifier, a = [y, z] and b = [y, x] order in the first dimension, but a and c = [z, y] don't in
        // the second. Reverting the second gives a back [x, y], which breaks the first, so it's reverted too.
        var first = Parse("stemfiles:roll_first", Checkerboard("stemfiles:roll_a", "stemfiles:roll_b"))
            .Dimension
            .Noise();
        var second = Parse("stemfiles:roll_second", Checkerboard("stemfiles:roll_a", "stemfiles:roll_c"))
            .Dimension
            .Noise();

        FeatureFile[] files =
        [
            Feature(PluginFeatures.PlacedFeature, "stemfiles:x", NoOp),
            Feature(PluginFeatures.PlacedFeature, "stemfiles:y", NoOp),
            Feature(PluginFeatures.PlacedFeature, "stemfiles:z", NoOp),
            Modifier("stemfiles:add_x", "add", "stemfiles:roll_b", "stemfiles:x"),
            Modifier("stemfiles:add_y", "add", "stemfiles:roll_c", "stemfiles:y"),
            Modifier("stemfiles:add_z", "add", "stemfiles:roll_a", "stemfiles:z"),
            Modifier("stemfiles:no_x", "remove", "stemfiles:roll_a", "stemfiles:x")
        ];

        var features = PluginFeatures.Build(files, biomes, [], logger, [first, second]);

        Assert.Empty(features.Unordered);

        // Both can be generated: ordering their features doesn't throw.
        foreach (var dimension in new[] { first, second })
        {
            FeatureSorter.Build(
                dimension.PossibleBiomes,
                biome => features.BiomeFeatures.GetValueOrDefault(biome.Name) ?? []);
        }

        var b = features.BiomeFeatures["stemfiles:roll_b"][0];
        Assert.Equal(["stemfiles:y"], b.Select(feature => feature.Identifier));
    }

    // A plugin biome placing the features in the raw_generation step.
    private static CodecFile Biome(string identifier, string features, Func<string, bool> isFeature)
    {
        var json = Glade.Replace("""[[], [], [], [], [], [], ["stemfiles:ore_marker"]]""", $"[{features}]");

        return PluginCodecs.Parse(PluginCodecs.Biome, identifier, "test", Bytes(json), isFeature);
    }

    private static string Checkerboard(string a, string b) => FixedStem.Replace(
        """{ "type": "minecraft:fixed", "biome": "stemfiles:glade" }""",
        $$"""{ "type": "minecraft:checkerboard", "biomes": ["{{a}}", "{{b}}"] }""");

    // A modifier adding a feature to a biome's raw_generation step, or removing it from every step.
    private static FeatureFile Modifier(string identifier, string type, string biome, string feature)
    {
        var features = type == "add" ? $$"""{ "raw_generation": "{{feature}}" }""" : $"\"{feature}\"";
        var json = $$"""
            {
              "biomes": "{{biome}}",
              "{{type}}": { "features": {{features}} }
            }
            """;

        return Feature(PluginFeatures.BiomeModifier, identifier, json);
    }

    private static FeatureFile Feature(string registry, string identifier, string json) =>
        PluginFeatures.Parse(registry, identifier, "test", Bytes(json));

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

        world.UseFolder(this.folder);
        world.Initialize(CodecRegistry.Dimensions.All["minecraft:overworld"]);
        world.CreateLevel(new LevelFiles { Data = { new NbtTag<string>("LevelName", "world") } });

        return world;
    }

    // The level the server creates for a plugin's dimension (see WorldManager.AddPluginDimensionsAsync).
    private static Dimension CreateLevel(
        World world,
        MojangDimension dimension,
        LevelStemGenerator? generator = null)
    {
        var level = new Dimension(
            NullLogger<Dimension>.Instance,
            null!,
            new TestOptionsMonitor<ServerConfiguration>(new ServerConfiguration()),
            null!,
            generator ?? new LevelStemGenerator(dimension),
            dimension.Identifier,
            world);

        level.Initialize(dimension.DimensionType);

        return level;
    }
}
