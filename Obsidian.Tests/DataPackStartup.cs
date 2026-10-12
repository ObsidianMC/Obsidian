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
        File.WriteAllText(Path.Join(pack, "pack.mcmeta"), """{ "pack": { "pack_format": 94, "description": "" } }""");
        File.WriteAllText(Path.Join(data, "minecraft", "dimension_type", "overworld.json"), lowOverworld);
        File.WriteAllText(Path.Join(data, "startuptest", "dimension", "plains.json"), plains);

        var logger = new PluginFeatureFiles.ListLogger<PluginManager>();
        var manager = CreateManager(logger);

        try
        {
            manager.FinishStartup(this.folder);

            Assert.Equal(-64, CodecRegistry.Dimensions.All["minecraft:overworld"].Element.MinY);

            var dimension = PluginDimensions.Find("startuptest:plains")!;
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
