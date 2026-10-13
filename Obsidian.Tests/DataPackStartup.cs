using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using System.Text;
using System.Threading.Tasks;
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
    private const string PackMetadata = """{ "pack": { "description": "", "min_format": 94, "max_format": 94 } }""";

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-startup-packs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(this.folder))
            Directory.Delete(this.folder, recursive: true);
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

        var logger = new PluginFeatureFiles.ListLogger<PluginManager>();
        var manager = CreateManager(logger);

        try
        {
            manager.FinishStartup(this.folder);

            Assert.Equal(-64, CodecRegistry.Dimensions.All["minecraft:overworld"].Element.MinY);

            var dimension = PluginDimensions.Find("startuptest:plains").Noise();
            Assert.Equal("minecraft:plains", Assert.Single(dimension.PossibleBiomes).Name);

            Assert.Contains(logger.Messages, message => message.Contains("Kept vanilla's dimension type"));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("Left out"));
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

        var logger = new PluginFeatureFiles.ListLogger<PluginManager>();
        var manager = CreateManager(logger);

        try
        {
            Assert.NotNull(await manager.LoadPluginAsync(plugin));
            manager.FinishStartup(Path.Join(this.folder, "packs"));

            Assert.Equal(0, CodecRegistry.Dimensions.All["minecraft:overworld"].Element.MinY);
            Assert.Equal("startuptest:tall", PluginDimensions.Find("minecraft:overworld").Noise().NoiseSettings);

            Assert.DoesNotContain(logger.Messages, message => message.Contains("Kept vanilla's dimension type"));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("Left out"));
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
    private static PluginManager CreateManager(ILogger<PluginManager> logger)
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
            logger,
            new ConfigurationBuilder().Build());
    }
}
