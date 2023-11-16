using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.Registries;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.WorldData;

public sealed class WorldManager : BackgroundService, IAsyncDisposable
{
    private readonly ILogger logger;
    private readonly Server server;
    private readonly Dictionary<string, World> worlds = new();
    private readonly List<ServerWorld> serverWorlds;

    public int GeneratingChunkCount => worlds.Sum(w => w.Value.ChunksToGen.Count);

    public int RegionCount => worlds.Sum(pair => pair.Value.Regions.Count);

    public int LoadedChunkCount => worlds.Sum(pair => pair.Value.Regions.Sum(x => x.Value.LoadedChunkCount));

    public World? DefaultWorld { get; private set; }

    public WorldManager(Server server, ILogger logger, List<ServerWorld> serverWorlds)
    {
        this.server = server;
        this.logger = logger;
        this.serverWorlds = serverWorlds;
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new BalancingTimer(1000, stoppingToken);

        // TODO: This should defenitly accept a cancellation token.
        // If Cancel is called, this method should stop within the configured timeout, otherwise code execution will simply stop here,
        // and server shutdown will not be handled correctly.
        // Load worlds on startup.
        await this.LoadWorldsAsync();

        while (await timer.WaitForNextTickAsync())
        {
            await Task.WhenAll(this.worlds.Values.Select(x => x.ManageChunksAsync()));
        }
    }

    public async Task LoadWorldsAsync()
    {
        foreach (var serverWorld in this.serverWorlds)
        {
            if (!server.WorldGenerators.TryGetValue(serverWorld.Generator, out var value))
            {
                this.logger.LogError("Unknown generator type {generator} for world {worldName}", serverWorld.Generator, serverWorld.Name);
                return;
            }

            var world = new World(serverWorld.Name, this.server, serverWorld.Seed, value);
            this.worlds.Add(world.Name, world);

            if (!CodecRegistry.TryGetDimension(serverWorld.DefaultDimension, out var defaultCodec) || !CodecRegistry.TryGetDimension("minecraft:overworld", out defaultCodec))
                throw new UnreachableException("Failed to get default dimension codec.");

            if (!await world.LoadAsync(defaultCodec))
            {
                this.logger.LogInformation("Creating new world: {worldName}...", serverWorld.Name);

                world.Init(defaultCodec);

                //TODO maybe make a method that takes in params
                foreach (var dimensionName in serverWorld.ChildDimensions)
                {
                    if (!CodecRegistry.TryGetDimension(dimensionName, out var codec))
                    {
                        this.logger.LogWarning("Failed to find dimension with the name {dimensionName}", dimensionName);
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

        }

        //No default world was defined so choose the first one to come up
        if (this.DefaultWorld == null)
            this.DefaultWorld = this.worlds.FirstOrDefault().Value;
    }

    public IReadOnlyCollection<World> GetAvailableWorlds() => this.worlds.Values.ToList().AsReadOnly();

    public bool TryGetWorld(string name, [NotNullWhen(true)] out World? world) => this.worlds.TryGetValue(name, out world);

    public Task TickWorldsAsync() => Task.WhenAll(this.worlds.Select(pair => pair.Value.DoWorldTickAsync()));
    public Task FlushLoadedWorldsAsync() => Task.WhenAll(this.worlds.Select(pair => pair.Value.FlushRegionsAsync()));


    public async ValueTask DisposeAsync()
    {
        foreach (var world in worlds)
        {
            await world.Value.DisposeAsync();
        }

        this.Dispose();
    }
}
