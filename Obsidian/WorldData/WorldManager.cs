using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.Hosting;
using Obsidian.Registries;
using Obsidian.Services;
using Obsidian.WorldData.Generators;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.WorldData;

public sealed class WorldManager : BackgroundService, IWorldManager
{
    private readonly ILogger logger;
    private readonly Dictionary<string, IWorld> worlds = new();
    private readonly List<ServerWorld> serverWorlds;
    private readonly ILoggerFactory loggerFactory;
    private readonly IServerEnvironment serverEnvironment;
    private readonly IServiceScope serviceScope;

    public int GeneratingChunkCount => worlds.Values.Sum(w => w.ChunksToGenCount);
    public int RegionCount => worlds.Values.Sum(pair => pair.RegionCount);
    public int LoadedChunkCount => worlds.Values.Sum(pair => pair.LoadedChunkCount);

    public IWorld DefaultWorld { get; private set; } = default!;

    public Dictionary<string, Type> WorldGenerators { get; } = new();

    public WorldManager(ILoggerFactory loggerFactory, IServiceProvider serviceProvider, IServerEnvironment serverEnvironment)
    {
        this.logger = loggerFactory.CreateLogger<WorldManager>();
        this.serverWorlds = serverEnvironment.ServerWorlds;
        this.loggerFactory = loggerFactory;
        this.serverEnvironment = serverEnvironment;
        this.serviceScope = serviceProvider.CreateScope();
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new BalancingTimer(1000, stoppingToken);

        this.RegisterDefaults();
        // TODO: This should defenitly accept a cancellation token.
        // If Cancel is called, this method should stop within the configured timeout, otherwise code execution will simply stop here,
        // and server shutdown will not be handled correctly.
        // Load worlds on startup.
        await this.LoadWorldsAsync();

        while (await timer.WaitForNextTickAsync())
        {
            await Task.WhenAll(this.worlds.Values.Cast<World>().Select(x => x.ManageChunksAsync()));
        }
    }

    /// <summary>
    /// Registers new world generator(s) to the server.
    /// </summary>
    /// <param name="entries">A compatible list of entries.</param>
    public void RegisterGenerator<T>() where T : IWorldGenerator, new()
    {
        var gen = new T();
        if (string.IsNullOrWhiteSpace(gen.Id))
            throw new InvalidOperationException($"Failed to get id for generator: {gen.Id}");

        if (this.WorldGenerators.TryAdd(gen.Id, typeof(T)))
            this.logger.LogDebug("Registered {generatorId}...", gen.Id);
    }

    public async Task LoadWorldsAsync()
    {
        foreach (var serverWorld in this.serverWorlds)
        {
            //var server = (Server)this.server;
            if (!this.WorldGenerators.TryGetValue(serverWorld.Generator, out var generatorType))
            {
                this.logger.LogError("Unknown generator type {generator} for world {worldName}", serverWorld.Generator, serverWorld.Name);
                return;
            }

            //TODO fix
            var world = new World(this.loggerFactory.CreateLogger($"World [{serverWorld.Name}]"), generatorType, this)
            {
                Configuration = this.serverEnvironment.Configuration,
                PacketBroadcaster = this.serviceScope.ServiceProvider.GetRequiredService<IPacketBroadcaster>(),
                Name = serverWorld.Name,
                Seed = serverWorld.Seed
            };

            world.InitGenerator();

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
        this.DefaultWorld ??= this.worlds.FirstOrDefault().Value;
    }

    public IReadOnlyCollection<IWorld> GetAvailableWorlds() => this.worlds.Values.ToList().AsReadOnly();

    public bool TryGetWorld(string name, [NotNullWhen(true)] out IWorld? world) => this.worlds.TryGetValue(name, out world);
    public bool TryGetWorld<TWorld>(string name, [NotNullWhen(true)] out TWorld? world) where TWorld : IWorld
    {
        if (this.worlds.TryGetValue(name, out var value))
        {
            world = (TWorld)value;
            return true;
        }

        world = default;
        return false;
    }

    public Task TickWorldsAsync() => Task.WhenAll(this.worlds.Select(pair => pair.Value.DoWorldTickAsync()));
    public Task FlushLoadedWorldsAsync() => Task.WhenAll(this.worlds.Select(pair => pair.Value.FlushRegionsAsync()));

    public async ValueTask DisposeAsync()
    {
        foreach (var world in this.worlds.Values)
        {
            await world.DisposeAsync();
        }

        this.serviceScope.Dispose();

        this.Dispose();
    }

    /// <summary>
    /// Registers the "obsidian-vanilla" entities and objects.
    /// </summary>
    /// Might be used for more stuff later so I'll leave this here - tides
    private void RegisterDefaults()
    {
        this.RegisterGenerator<SuperflatGenerator>();
        this.RegisterGenerator<OverworldGenerator>();
        this.RegisterGenerator<IslandGenerator>();
        this.RegisterGenerator<EmptyWorldGenerator>();
    }
}
