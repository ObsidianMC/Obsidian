using Microsoft.Extensions.Logging;
using Obsidian.Hosting;
using Obsidian.Utilities.Registry;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.WorldData;

public sealed class WorldManager
{
    private readonly ILogger logger;
    private readonly IServerEnvironment _env;
    private readonly Server server;
    private readonly Dictionary<string, World> worlds = new();

    public int GeneratingChunkCount => worlds.SelectMany(pair => pair.Value.Regions.Where(r => r.Value is not null)).Sum(r => r.Value.LoadedChunkCount);

    public int RegionCount => worlds.Sum(pair => pair.Value.Regions.Count);

    public int LoadedChunkCount => worlds.Sum(pair => pair.Value.Regions.Sum(x => x.Value.LoadedChunkCount));

    public World DefaultWorld { get; private set; }

    public WorldManager(Server server, ILogger<WorldManager> logger, IServerEnvironment env)
    {
        this.server = server;
        this.logger = logger;
        this._env = env;
    }

    public async Task LoadWorldsAsync()
    {
        foreach (var serverWorld in _env.ServerWorlds)
        {
            if (!server.WorldGenerators.TryGetValue(serverWorld.Generator, out var value))
                logger.LogWarning($"Unknown generator type {serverWorld.Generator}");

            var world = new World(serverWorld.Name, this.server, serverWorld.Seed, value);

            if (!Registry.TryGetDimensionCodec(serverWorld.DefaultDimension, out var defaultCodec) || !Registry.TryGetDimensionCodec("minecraft:overworld", out defaultCodec))
                throw new InvalidOperationException("Failed to get default dimension codec.");

            if (!await world.LoadAsync(defaultCodec))
            {
                logger.LogInformation($"Creating new world: {serverWorld.Name}...");

                world.Init(defaultCodec);

                //TODO maybe make a method that takes in params
                foreach (var dimensionName in serverWorld.ChildDimensions)
                {
                    if (!Registry.TryGetDimensionCodec(dimensionName, out var codec))
                    {
                        this.server.Logger.LogWarning($"Failed to find dimension with the name {dimensionName}");
                        continue;
                    }

                    // Don't have any dimension generators yet so we'll just stick with overworld
                    // TODO create dimension generators
                    world.RegisterDimension(codec, "overworld");
                }

                await world.GenerateWorldAsync(true);

                await world.SaveAsync();
            }

            if (serverWorld.Default && this.DefaultWorld == null)
                this.DefaultWorld = world;

            this.worlds.Add(world.Name, world);
        }

        //No default world was defined so choose the first one to come up
        if (this.DefaultWorld == null)
            this.DefaultWorld = this.worlds.FirstOrDefault().Value;
    }

    public IReadOnlyCollection<World> GetAvailableWorlds() => this.worlds.Values.ToList().AsReadOnly();

    public bool TryGetWorld(string name, [NotNullWhen(true)] out World? world) => this.worlds.TryGetValue(name, out world);

    public Task TickWorldsAsync() => Task.WhenAll(this.worlds.Select(pair => pair.Value.DoWorldTickAsync()));
    public Task FlushLoadedWorldsAsync() => Task.WhenAll(this.worlds.Select(pair => pair.Value.FlushRegionsAsync()));
}
