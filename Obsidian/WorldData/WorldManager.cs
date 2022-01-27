using Microsoft.Extensions.Logging;
using Obsidian.Utilities.Registry;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.WorldData;

public sealed class WorldManager
{
    private readonly ILogger logger;
    private readonly Server server;
    private readonly Dictionary<string, World> worlds = new();
    private readonly List<ServerWorld> serverWorlds;

    public int GeneratingChunkCount => worlds.SelectMany(pair => pair.Value.Regions.Where(r => r.Value is not null)).Sum(r => r.Value.LoadedChunkCount);

    public int RegionCount => worlds.Sum(pair => pair.Value.Regions.Count);

    public int LoadedChunkCount => worlds.Sum(pair => pair.Value.Regions.Sum(x => x.Value.LoadedChunkCount));

    public World DefaultWorld { get; private set; }

    public WorldManager(Server server, ILogger logger, List<ServerWorld> serverWorlds)
    {
        this.server = server;
        this.logger = logger;
        this.serverWorlds = serverWorlds;
    }

    public async Task LoadWorldsAsync()
    {
        foreach (var serverWorld in serverWorlds)
        {
            if (!server.WorldGenerators.TryGetValue(serverWorld.Generator, out var value))
                logger.LogWarning($"Unknown generator type {serverWorld.Generator}");

            var world = new World(serverWorld.Name, server, serverWorld.Seed, value);

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
                        server.Logger.LogWarning($"Failed to find dimension with the name {dimensionName}");
                        continue;
                    }

                    // Don't have any dimension generators yet so we'll just stick with overworld
                    // TODO create dimension generators
                    world.RegisterDimension(codec, "overworld");
                }

                await world.GenerateWorldAsync(true);

                await world.SaveAsync();
            }

            if (serverWorld.Default && DefaultWorld == null)
                DefaultWorld = world;

            worlds.Add(world.Name, world);
        }

        //No default world was defined so choose the first one to come up
        if (DefaultWorld == null)
            DefaultWorld = worlds.FirstOrDefault().Value;
    }

    public IReadOnlyCollection<World> GetAvailableWorlds() => worlds.Values.ToList().AsReadOnly();

    public bool TryGetWorld(string name, [NotNullWhen(true)] out World? world) => worlds.TryGetValue(name, out world);

    public Task TickWorldsAsync() => Task.WhenAll(worlds.Select(pair => pair.Value.DoWorldTickAsync()));
    public Task FlushLoadedWorldsAsync() => Task.WhenAll(worlds.Select(pair => pair.Value.FlushRegionsAsync()));
}
