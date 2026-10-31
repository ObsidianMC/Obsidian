using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Registries;
using Obsidian.Commands.Framework;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Services;
using Obsidian.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Tests that override vanilla's dimension types for a while, which run alone: other tests generate vanilla's
/// dimensions with them.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VanillaDimensionTypesCollection
{
    public const string Name = "Vanilla dimension types";
}

/// <summary>
/// The end of startup, which reads the data packs folder after the plugins and settles vanilla's dimension types.
/// </summary>
[Collection(VanillaDimensionTypesCollection.Name)]
public sealed class DataPackStartup : IDisposable
{
    private const string PackMetadata = """{ "pack": { "description": "", "min_format": 121, "max_format": 121 } }""";

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-startup-packs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(this.folder))
            Directory.Delete(this.folder, recursive: true);
    }

    [Fact]
    public async Task Format121PacksOverridePluginsAndSurviveTheirUnload()
    {
        bool[] cases =
        [
            false,
            true
        ];

        await FixtureCases.AssertAllAsync(
            cases,
            row => $"zipped={row}",
            AssertFormat121PacksOverridePluginsAndSurviveTheirUnload);
    }

    private async Task AssertFormat121PacksOverridePluginsAndSurviveTheirUnload(bool zipped)
    {
        using var biomes = Format121Data.Read("biome");
        using var metadata = Format121Data.Read("pack");
        var biome = JsonNode.Parse(biomes.RootElement.GetProperty("minecraft:plains").GetRawText())!.AsObject();

        // Generation is tested separately; this pack exercises the current biome and spawn codecs at startup.
        biome["features"] = new JsonArray();
        biome["carvers"] = new JsonArray();
        biome["temperature"] = 0.25f;
        var rootBiome = biome.ToJsonString();
        biome["temperature"] = 0.75f;
        var overlayBiome = biome.ToJsonString();

        var packMetadata = JsonNode.Parse(metadata.RootElement.GetRawText())!.AsObject();
        packMetadata["overlays"] = JsonNode.Parse("""
            { "entries": [
              { "directory": "current", "min_format": [121, 0], "max_format": [121, 0] },
              { "directory": "future", "min_format": [121, 1], "max_format": [121, 1] }
            ] }
            """);

        const string biomePath = "data/minecraft/worldgen/biome/plains.json";
        var files = new Dictionary<string, string>
        {
            ["pack.mcmeta"] = packMetadata.ToJsonString(),
            [biomePath] = rootBiome,
            [$"current/{biomePath}"] = overlayBiome,
            [$"future/{biomePath}"] = "{}"
        };
        // Each case gets its own packs and plugin, so the other case's pack can't stand in for this one's.
        var root = Path.Join(this.folder, zipped ? "zipped" : "directory");
        var packs = Path.Join(root, "packs");
        Directory.CreateDirectory(packs);

        if (zipped)
        {
            using var zip = ZipFile.Open(Path.Join(packs, "format121.zip"), ZipArchiveMode.Create);

            foreach (var (path, json) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(json);
            }
        }
        else
        {
            foreach (var (path, json) in files)
            {
                var file = Path.Join(packs, "format121", path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, json);
            }
        }

        biome["temperature"] = 0.5f;
        var pluginFiles = new Dictionary<string, byte[]>
        {
            [biomePath] = Encoding.UTF8.GetBytes(biome.ToJsonString())
        };
        var pluginPath = Path.Join(root, "probe.obby");
        File.WriteAllBytes(pluginPath, PluginLoading.ProbeArchive("1.0.0", pluginFiles));
        var vanilla = CodecRegistry.Biomes.Plains.Element;
        var manager = CreateManager();

        try
        {
            var plugin = await manager.LoadPluginAsync(pluginPath);
            Assert.NotNull(plugin);
            Assert.Equal(0.5f, CodecRegistry.Biomes.Plains.Element.Temperature);

            manager.FinishStartup(packs);

            var applied = CodecRegistry.Biomes.Plains.Element;
            Assert.Equal(0.75f, applied.Temperature);
            Assert.NotEmpty(applied.Spawners);
            Assert.NotNull(applied.Spawners["creature"][0].Count);

            await manager.UnloadPluginAsync(plugin);

            Assert.Same(applied, CodecRegistry.Biomes.Plains.Element);
        }
        finally
        {
            await manager.DisposeAsync();
            PluginCodecs.Apply([], NullLogger.Instance, levelsCreated: false);
            PluginSpawns.Apply([], [], NullLogger.Instance);
            Directory.Delete(root, recursive: true);
        }

        Assert.Same(vanilla, CodecRegistry.Biomes.Plains.Element);
    }

    [Fact]
    public void Format121OverlayRangesSelectCurrentMinor()
    {
        (string Min, string Max, bool Applies)[] cases =
        [
            ("121", "121", true),
            ("[121, 0]", "[121, 0]", true),
            ("[120, 9]", "[120, 9]", false),
            ("[121, 1]", "[121, 1]", false)
        ];

        FixtureCases.AssertAll(
            cases,
            row => $"min={row.Min}, max={row.Max}, applies={row.Applies}",
            row => AssertFormat121OverlayRangesSelectCurrentMinor(row.Min, row.Max, row.Applies));
    }

    private static void AssertFormat121OverlayRangesSelectCurrentMinor(string min, string max, bool applies)
    {
        using var vanilla = Format121Data.Read("pack");
        var metadata = JsonNode.Parse(vanilla.RootElement.GetRawText())!.AsObject();
        metadata["overlays"] = JsonNode.Parse($$"""
            { "entries": [{ "directory": "selected", "min_format": {{min}}, "max_format": {{max}} }] }
            """);
        var logger = new PluginFeatureFiles.ListLogger();

        var read = DataPackMetadata.Read("format121", Encoding.UTF8.GetBytes(metadata.ToJsonString()), logger);

        Assert.Equal(applies, read.Overlays.Contains("selected"));
        Assert.Empty(logger.Messages);
    }

    [Fact(DisplayName = "A stem startup can't read until a dimension type override is left out is read once it is")]
    public async Task ReadsStemsAgainOnceDimensionTypesSettle()
    {
        // The overworld's type from y 0, which vanilla's overworld noise settings, from -64, don't fit: the stem using
        // them can't be read until startup leaves the type override out.
        const string lowOverworld = """
            {
              "has_skylight": true,
              "has_ceiling": false,
              "has_ender_dragon_fight": false,
              "coordinate_scale": 1,
              "min_y": 0,
              "height": 256,
              "logical_height": 256,
              "infiniburn": "#minecraft:infiniburn_overworld",
              "ambient_light": 0,
              "monster_spawn_light_level": 7,
              "monster_spawn_block_light_limit": 0
            }
            """;

        const string plains = """
            {
              "type": "minecraft:overworld",
              "generator": {
                "type": "minecraft:noise",
                "settings": "minecraft:overworld",
                "biome_source": { "type": "minecraft:fixed", "biome": "minecraft:plains" }
              }
            }
            """;

        var pack = Path.Join(this.folder, "plains");
        var data = Path.Join(pack, "data");
        Directory.CreateDirectory(Path.Join(data, "minecraft", "dimension_type"));
        Directory.CreateDirectory(Path.Join(data, "startuptest", "dimension"));
        File.WriteAllText(Path.Join(pack, "pack.mcmeta"), PackMetadata);
        File.WriteAllText(Path.Join(data, "minecraft", "dimension_type", "overworld.json"), lowOverworld);
        File.WriteAllText(Path.Join(data, "startuptest", "dimension", "plains.json"), plains);

        var manager = CreateManager();

        try
        {
            manager.FinishStartup(this.folder);

            Assert.Equal(-64, CodecRegistry.Dimensions.All["minecraft:overworld"].Element.MinY);

            var dimension = PluginDimensions.Find("startuptest:plains").Noise();
            Assert.Equal("minecraft:plains", Assert.Single(dimension.PossibleBiomes).Name);
        }
        finally
        {
            await manager.DisposeAsync();
            PluginCodecs.Apply([], NullLogger.Instance, levelsCreated: false);
            PluginDimensions.Apply([], NullLogger.Instance);
            PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance);
        }
    }

    [Fact(DisplayName = "A vanilla stem startup kept settles its type with the noise settings a later pack adds")]
    public async Task SettlesTypesWithTheNoiseSettingsOfKeptStems()
    {
        // A plugin's overworld stem names noise settings from y 0 to 511 that only a data pack read after it adds,
        // with the overworld's type over the same range: the type fits the stem, so it stays.
        const string tallOverworld = """
            {
              "has_skylight": true,
              "has_ceiling": false,
              "has_ender_dragon_fight": false,
              "coordinate_scale": 1,
              "min_y": 0,
              "height": 512,
              "logical_height": 512,
              "infiniburn": "#minecraft:infiniburn_overworld",
              "ambient_light": 0,
              "monster_spawn_light_level": 7,
              "monster_spawn_block_light_limit": 0
            }
            """;

        const string tallSettings = """
            {
              "sea_level": 63,
              "disable_mob_generation": false,
              "aquifers_enabled": false,
              "ore_veins_enabled": false,
              "legacy_random_source": false,
              "default_block": { "Name": "minecraft:stone" },
              "default_fluid": { "Name": "minecraft:water", "Properties": { "level": "0" } },
              "noise": { "min_y": 0, "height": 512, "size_horizontal": 1, "size_vertical": 2 },
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
                "final_density": 0,
                "vein_toggle": 0,
                "vein_ridged": 0,
                "vein_gap": 0
              },
              "spawn_target": [],
              "surface_rule": { "type": "minecraft:sequence", "sequence": [] }
            }
            """;

        const string overworld = """
            {
              "type": "minecraft:overworld",
              "generator": {
                "type": "minecraft:noise",
                "settings": "startuptest:tall",
                "biome_source": { "type": "minecraft:fixed", "biome": "minecraft:plains" }
              }
            }
            """;

        var pack = Path.Join(this.folder, "packs", "tall");
        var data = Path.Join(pack, "data");
        Directory.CreateDirectory(Path.Join(data, "minecraft", "dimension_type"));
        Directory.CreateDirectory(Path.Join(data, "startuptest", "worldgen", "noise_settings"));
        File.WriteAllText(Path.Join(pack, "pack.mcmeta"), PackMetadata);
        File.WriteAllText(Path.Join(data, "minecraft", "dimension_type", "overworld.json"), tallOverworld);
        File.WriteAllText(Path.Join(data, "startuptest", "worldgen", "noise_settings", "tall.json"), tallSettings);

        var plugin = Path.Join(this.folder, "probe.obby");
        var stem = new Dictionary<string, byte[]>
        {
            ["data/minecraft/dimension/overworld.json"] = Encoding.UTF8.GetBytes(overworld)
        };
        await File.WriteAllBytesAsync(plugin, PluginLoading.ProbeArchive("1.0.0", stem));

        var manager = CreateManager();

        try
        {
            Assert.NotNull(await manager.LoadPluginAsync(plugin));
            manager.FinishStartup(Path.Join(this.folder, "packs"));

            Assert.Equal(0, CodecRegistry.Dimensions.All["minecraft:overworld"].Element.MinY);
            Assert.Equal("startuptest:tall", PluginDimensions.Find("minecraft:overworld").Noise().NoiseSettings);
        }
        finally
        {
            await manager.DisposeAsync();
            PluginCodecs.Apply([], NullLogger.Instance, levelsCreated: false);
            PluginNoise.Apply([], [], NullLogger.Instance);
            PluginDimensions.Apply([], NullLogger.Instance);
            PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance);
        }
    }

    // A plugin manager with no plugins, as the server creates it, during startup: no world is loaded yet.
    private static PluginManager CreateManager()
    {
        var world = TestWorldFactory.Create($"fake-server-{Guid.NewGuid():N}");
        var server = new FakeServer(world, worldManager: new FakeWorldManager(world, worldsLoaded: false));

        var services = new ServiceCollection();
        services.AddOptions<ServerConfiguration>();
        services.AddSingleton<IServer>(server);
        var provider = services.BuildServiceProvider();

        var dispatcher = new EventDispatcher(NullLogger<EventDispatcher>.Instance, provider);
        var commands = new CommandHandler(provider, NullLogger<CommandHandler>.Instance);

        return new PluginManager(
            server,
            provider,
            dispatcher,
            commands,
            NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());
    }
}
