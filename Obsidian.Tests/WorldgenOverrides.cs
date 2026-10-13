using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.Noise;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Registries.Codecs;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Features;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Data packs' and plugins' files overriding vanilla's entries, like vanilla's data packs. The golden chunk tests check
/// that worlds without overrides generate as before.
/// </summary>
/// <remarks>
/// In the plugin tests' collection, as some apply files to the server's registries.
/// </remarks>
[Collection(nameof(PluginTagFiles))]
public sealed class WorldgenOverrides : IDisposable
{
    private const string PackMetadata = """{ "pack": { "pack_format": 94, "description": "" } }""";

    private const string FixedPlainsStem = """
        {
          "type": "minecraft:overworld",
          "generator": {
            "type": "minecraft:noise",
            "settings": "minecraft:overworld",
            "biome_source": { "type": "minecraft:fixed", "biome": "minecraft:plains" }
          }
        }
        """;

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-data-packs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(this.folder))
            Directory.Delete(this.folder, recursive: true);
    }

    [Fact(DisplayName = "A data pack overriding the overworld's parameter list generates the nether's biomes there")]
    public void DataPackOverridesTheOverworldsParameterList()
    {
        var pack = Path.Join(this.folder, "nether_overworld");
        var list = Path.Join(pack, "data", "minecraft", "worldgen", "multi_noise_biome_source_parameter_list");
        Directory.CreateDirectory(list);
        File.WriteAllText(Path.Join(pack, "pack.mcmeta"), PackMetadata);
        File.WriteAllText(Path.Join(list, "overworld.json"), """{ "preset": "minecraft:nether" }""");

        // Not a pack: no pack.mcmeta.
        Directory.CreateDirectory(Path.Join(this.folder, "notes"));

        var logger = new PluginFeatureFiles.ListLogger();
        var opened = Assert.Single(DataPack.OpenAll(this.folder, _ => true, logger));
        var files = PluginNoise.FindFiles(opened.Files.Paths)
            .Select(file => PluginNoise.Parse(file.Registry, file.Identifier, file.Path, opened.Files.Read(file.Path)!))
            .ToList();

        var noise = PluginNoise.Build(files, [], logger);

        // Without overrides, the overworld keeps its compiled dimension.
        Assert.Null(PluginDimensions.ReadVanilla("minecraft:overworld", WorldgenNoise.Vanilla));

        var overworld = PluginDimensions.ReadVanilla("minecraft:overworld", noise)!.Dimension;
        Assert.Equal(MultiNoiseBiomeSource.NetherBiomes, overworld.PossibleBiomes);
        Assert.Same(MojangDimension.Overworld.Settings, overworld.Settings);

        // Without features, so the chunk is only the terrain and biomes.
        var noFeatures = new WorldgenFeatureSet(
            FrozenDictionary<string, ConfiguredFeatureBase>.Empty,
            FrozenDictionary<string, PlacedFeature>.Empty,
            FrozenDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>>.Empty,
            FrozenDictionary<PlacedFeature, ImmutableArray<PluginContainer>>.Empty);

        var builder = new ChunkBuilder(overworld, 1234, generateStructures: false, () => noFeatures);
        var chunk = new FullChunks(builder, overworld).Get(0, 0);

        Assert.Contains(chunk.GetBiome(0, 64, 0), MultiNoiseBiomeSource.NetherBiomes);
        Assert.Single(logger.Messages, message => message.Contains("notes"));
    }

    [Fact(DisplayName = "Overriding a vanilla configured feature changes the vanilla placed features biomes place")]
    public void OverridesReachVanillaFeaturesNamingThem()
    {
        const string emeraldOre = """
            {
              "type": "minecraft:ore",
              "config": {
                "size": 4,
                "discard_chance_on_air_exposure": 0,
                "targets": [
                  {
                    "target": { "predicate_type": "minecraft:tag_match", "tag": "minecraft:stone_ore_replaceables" },
                    "state": { "Name": "minecraft:emerald_block" }
                  }
                ]
              }
            }
            """;

        var logger = new PluginFeatureFiles.ListLogger();
        var file = PluginFeatures.Parse(
            PluginFeatures.ConfiguredFeature,
            "minecraft:ore_diamond_small",
            "test",
            Encoding.UTF8.GetBytes(emeraldOre));

        var features = PluginFeatures.Build([file], [], [], logger, [MojangDimension.Overworld]);
        Assert.Empty(logger.Messages);

        var placed = features.BiomeFeatures["minecraft:plains"]
            .SelectMany(step => step)
            .Single(feature => feature.Identifier == "minecraft:ore_diamond");

        // The vanilla placed feature naming it is read again around the override; unrelated ones stay compiled.
        Assert.NotSame(PlacedFeatures.All["minecraft:ore_diamond"], placed);
        Assert.Same(features.ConfiguredFeatures["minecraft:ore_diamond_small"], placed.Feature);
        Assert.Contains(
            PlacedFeatures.All["minecraft:ore_coal_upper"],
            features.BiomeFeatures["minecraft:plains"].SelectMany(step => step));

        // What places a feature by identifier, like the pale oak's moss decorator, finds the override too.
        Assert.Same(placed.Feature, features.FindConfigured("minecraft:ore_diamond_small"));
        Assert.Same(
            ConfiguredFeatures.All["minecraft:pale_moss_patch"],
            features.FindConfigured("minecraft:pale_moss_patch"));
    }

    [Fact(DisplayName = "The noises the surface builder reads by name follow overrides")]
    public void SurfaceNoisesFollowOverrides()
    {
        var logger = new PluginFeatureFiles.ListLogger();
        var file = PluginNoise.Parse(
            PluginNoise.Noise,
            "minecraft:badlands_pillar",
            "test",
            """{ "firstOctave": -3, "amplitudes": [1] }"""u8);

        var noise = PluginNoise.Build([file], [], logger);
        Assert.Empty(logger.Messages);

        // No vanilla file names it, but the overworld is read at runtime to take it.
        var overworld = PluginDimensions.ReadVanilla("minecraft:overworld", noise)!.Dimension;
        var pillar = new RandomState(overworld.Settings, 1234, overworld.Noises)
            .GetOrCreateNoise("minecraft:badlands_pillar");
        var vanilla = new RandomState(overworld.Settings, 1234).GetOrCreateNoise("minecraft:badlands_pillar");

        Assert.Equal(-3, pillar.FirstOctave);
        Assert.Equal(vanilla.Key, pillar.Key);
        Assert.NotEqual(vanilla.FirstOctave, pillar.FirstOctave);
    }

    [Fact(DisplayName = "An overridden vanilla synced entry is sent with its data, and by name once not overridden")]
    public void OverriddenVanillaEntriesAreSentWithData()
    {
        static CodecFile Biome(string temperature) => PluginCodecs.Parse(
            PluginCodecs.Biome,
            "minecraft:plains",
            "test",
            Encoding.UTF8.GetBytes($$"""
                {
                  "has_precipitation": true,
                  "temperature": {{temperature}},
                  "downfall": 0.4,
                  "effects": { "water_color": "#3f76e4" },
                  "carvers": [],
                  "features": [],
                  "spawners": {},
                  "spawn_costs": {}
                }
                """));

        // A table of its own, as the server's is shared with other tests.
        var vanilla = (BiomeElement)Biome("0.8").Element;
        var plains = new BiomeCodec { Name = "minecraft:plains", Id = 0, Element = vanilla };
        var table = new CodecTable<BiomeCodec>([plains]);
        var hot = Biome("2");

        var changed = table.Apply(
            [new PluginCodecEntry<BiomeElement>(hot.Identifier, (BiomeElement)hot.Element, hot.Json, hot.Data)],
            (name, id, element) => new BiomeCodec { Name = name, Id = id, Element = element },
            codec => codec.Element,
            (codec, element) => codec.Element = element);

        Assert.True(changed);
        Assert.True(table.IsOverridden("minecraft:plains"));
        Assert.Same(plains, table.All["minecraft:plains"]);
        Assert.Equal(2f, plains.Element.Temperature);
        Assert.Same(hot.Data, Assert.Single(table.NetworkEntries).Data);

        table.Apply<BiomeElement>(
            [],
            (name, id, element) => new BiomeCodec { Name = name, Id = id, Element = element },
            codec => codec.Element,
            (codec, element) => codec.Element = element);

        Assert.False(table.IsOverridden("minecraft:plains"));
        Assert.Same(vanilla, plains.Element);
        Assert.Null(Assert.Single(table.NetworkEntries).Data);
    }

    [Fact(DisplayName = "A data pack over its limits is left out, and the files Obsidian doesn't read aren't read")]
    public void DataPacksAreReadWithinLimits()
    {
        var spaces = new string(' ', 4096);
        Directory.CreateDirectory(this.folder);

        this.Zip("big.zip", new()
        {
            ["pack.mcmeta"] = PackMetadata,
            ["data/minecraft/worldgen/noise/big.json"] = $"{{}}{spaces}"
        });

        this.Zip("recipes.zip", new()
        {
            ["pack.mcmeta"] = PackMetadata,
            ["data/minecraft/recipe/big.json"] = spaces
        });

        var logger = new PluginFeatureFiles.ListLogger();
        var limits = new DataPackLimits(Files: 8, FileSize: 1024, TotalSize: 2048);
        var pack = Assert.Single(DataPack.OpenAll(this.folder, path => path.Contains("/worldgen/"), logger, limits));

        Assert.Equal("recipes.zip", pack.Files.Name);
        Assert.Contains("data/minecraft/recipe/big.json", pack.Files.Paths);
        Assert.Null(pack.Files.Read("data/minecraft/recipe/big.json"));
        Assert.Contains(logger.Messages, message => message.Contains("big.zip") && message.Contains("1024 bytes"));
    }

    [Fact(DisplayName = "A dimension follows later overrides of the data its stem names, and its files, until taken")]
    public void DimensionsFollowTheirFilesUntilTaken()
    {
        const string identifier = "overridetest:world";
        const string stem = """
            {
              "type": "minecraft:overworld",
              "generator": {
                "type": "minecraft:noise",
                "settings": "minecraft:overworld",
                "biome_source": { "type": "minecraft:multi_noise", "preset": "overridetest:climate" }
              }
            }
            """;

        var logger = new PluginFeatureFiles.ListLogger();
        var pluginList = PluginNoise.Parse(
            PluginNoise.ParameterList,
            "overridetest:climate",
            "plugin",
            Encoding.UTF8.GetBytes("""{ "preset": "minecraft:overworld" }"""));
        var packList = PluginNoise.Parse(
            PluginNoise.ParameterList,
            "overridetest:climate",
            "pack",
            Encoding.UTF8.GetBytes("""{ "preset": "minecraft:nether" }"""));

        try
        {
            // A plugin loads with the stem and its parameter list, then a data pack read after it overrides the list.
            PluginNoise.Apply([pluginList], [], logger);
            var file = PluginDimensions.Parse(identifier, "plugin", Encoding.UTF8.GetBytes(stem));
            PluginDimensions.Apply([file], logger);

            PluginNoise.Apply([pluginList, packList], [], logger);
            PluginDimensions.Apply([file], logger);

            Assert.Equal(MultiNoiseBiomeSource.NetherBiomes, PluginDimensions.Find(identifier)!.PossibleBiomes);

            // Another plugin's stem wins, then that plugin unloads: the first stem wins again, read from its bytes.
            var fixedStem = stem.Replace(
                """{ "type": "minecraft:multi_noise", "preset": "overridetest:climate" }""",
                """{ "type": "minecraft:fixed", "biome": "minecraft:plains" }""");
            var other = PluginDimensions.Parse(identifier, "other", Encoding.UTF8.GetBytes(fixedStem));

            PluginDimensions.Apply([file, other], logger);
            Assert.Equal("minecraft:plains", Assert.Single(PluginDimensions.Find(identifier)!.PossibleBiomes).Name);

            PluginDimensions.Apply([file], logger);
            var dimension = PluginDimensions.Find(identifier)!;

            Assert.False(dimension.IsReleased);
            Assert.Equal(MultiNoiseBiomeSource.NetherBiomes, dimension.PossibleBiomes);
        }
        finally
        {
            PluginNoise.Apply([], [], NullLogger.Instance);
            PluginDimensions.Apply([], NullLogger.Instance);
        }

        // No level took it, so it went with its files.
        Assert.Null(PluginDimensions.Find(identifier));
    }

    [Fact(DisplayName = "A level can't take a dimension that went since its snapshot, which can still come back")]
    public void TakingAGoneDimensionLeavesItUntaken()
    {
        const string identifier = "overridetest:gone";
        var stem = Encoding.UTF8.GetBytes(FixedPlainsStem);
        var logger = new PluginFeatureFiles.ListLogger();

        try
        {
            PluginDimensions.Apply([PluginDimensions.Parse(identifier, "plugin", stem)], logger);
            var snapshot = PluginDimensions.Find(identifier)!;

            // The plugin unloads before the level is created: its dimension goes, released.
            PluginDimensions.Apply([], logger);
            Assert.Null(PluginDimensions.Take(snapshot));

            // Back, it follows its file again, as nothing took it.
            PluginDimensions.Apply([PluginDimensions.Parse(identifier, "plugin", stem)], logger);
            Assert.False(PluginDimensions.Find(identifier)!.IsReleased);
        }
        finally
        {
            PluginDimensions.Apply([], NullLogger.Instance);
        }

        Assert.Null(PluginDimensions.Find(identifier));
    }

    [Fact(DisplayName = "A level whose generator can't be built gives back its dimension and biome regions")]
    public void FailedGeneratorGivesItsDimensionBack()
    {
        const string identifier = "overridetest:broken";
        var logger = new PluginFeatureFiles.ListLogger();
        var region = PluginBiomeRegions.Parse(
            "overridetest:crimson",
            "plugin",
            Encoding.UTF8.GetBytes($$"""
                {
                  "dimension": "{{identifier}}",
                  "weight": 10,
                  "replace": { "minecraft:plains": "minecraft:crimson_forest" }
                }
                """));

        try
        {
            PluginBiomeRegions.Apply([region], logger);
            PluginDimensions.Apply(
                [PluginDimensions.Parse(identifier, "plugin", Encoding.UTF8.GetBytes(FixedPlainsStem))],
                logger);

            // Building the generator fails after its biome source fixed the regions, before anything generated, as
            // when its biomes' features can't be ordered: its level isn't created.
            var generator = new LevelStemGenerator(PluginDimensions.Find(identifier)!)
            {
                Features = () => throw new InvalidDataException("The features can't be ordered.")
            };

            var level = $"broken-{Guid.NewGuid():N}";
            Assert.Throws<InvalidDataException>(() => TestWorldFactory.Create(level, generator));
            Assert.False(generator.IsTaken);

            // Its regions follow the plugins again, which dropped the region, and a corrected stem applies at once
            // rather than after a restart.
            PluginBiomeRegions.Apply([], logger);
            Assert.Equal("minecraft:plains", Assert.Single(generator.Dimension.PossibleBiomes).Name);

            var desert = FixedPlainsStem.Replace("minecraft:plains", "minecraft:desert");
            PluginDimensions.Apply(
                [PluginDimensions.Parse(identifier, "plugin", Encoding.UTF8.GetBytes(desert))],
                logger);

            var dimension = PluginDimensions.Find(identifier)!;
            Assert.Equal("minecraft:desert", Assert.Single(dimension.PossibleBiomes).Name);
            Assert.DoesNotContain(logger.Messages, message => message.Contains("restart"));
        }
        finally
        {
            PluginBiomeRegions.Apply([], NullLogger.Instance);
            PluginDimensions.Apply([], NullLogger.Instance);
        }

        Assert.Null(PluginDimensions.Find(identifier));
    }

    [Fact(DisplayName = "Noise settings generating outside a dimension type's build range are found")]
    public void FindsNoiseSettingsOutsideTheBuildRange()
    {
        var overworld = CodecRegistry.Dimensions.All["minecraft:overworld"];
        var settings = MojangDimension.Overworld.Settings;
        var lower = new DimensionCodec
        {
            Name = overworld.Name,
            Id = overworld.Id,
            Element = overworld.Element with { MinY = 0, Height = 256, LogicalHeight = 256 }
        };

        Assert.Null(LevelStemData.OutsideBuildRange("minecraft:overworld", settings, overworld));
        Assert.Contains(
            "from y -64 to 319, outside the build range of minecraft:overworld (0 to 255)",
            LevelStemData.OutsideBuildRange("minecraft:overworld", settings, lower));

        // Cells of 12 blocks: the lowest is the one holding y -64, which fills from -72, below the build range.
        var tallCells = new NoiseSetting
        {
            Noise = new NoiseConfiguration
            {
                MinY = -64,
                Height = 384,
                SizeHorizontal = 1,
                SizeVertical = 3
            }
        };

        Assert.Contains(
            "from y -72 (min_y -64 rounded down to a whole cell of 12 blocks) to 319",
            LevelStemData.OutsideBuildRange("tall cells", tallCells, overworld));
    }

    private void Zip(string name, Dictionary<string, string> files)
    {
        using var zip = ZipFile.Open(Path.Join(this.folder, name), ZipArchiveMode.Create);

        foreach (var (path, text) in files)
        {
            using var writer = new StreamWriter(zip.CreateEntry(path).Open());
            writer.Write(text);
        }
    }
}
