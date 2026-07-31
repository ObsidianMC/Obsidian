using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.Hosting;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

public sealed class WorldManager(ILogger<WorldManager> logger, IServiceProvider serviceProvider, IOptionsMonitor<ServerConfiguration> configuration,
    IServerEnvironment serverEnvironment, ILevelFactory levelFactory) : BackgroundService, IWorldManager
{
    private readonly ILogger<WorldManager> logger = logger;
    private readonly Dictionary<string, IWorld> worlds = [];
    private readonly IOptionsMonitor<ServerConfiguration> configuration = configuration;
    private readonly IServerEnvironment serverEnvironment = serverEnvironment;
    private readonly ILevelFactory levelFactory = levelFactory;
    private readonly IServiceScope serviceScope = serviceProvider.CreateScope();

    public bool ReadyToJoin { get; private set; }

    public int GeneratingChunkCount => worlds.Values.Sum(w => w.ChunksToGenCount);
    public int RegionCount => worlds.Values.Sum(pair => pair.RegionCount);
    public int LoadedChunkCount => worlds.Values.Sum(pair => pair.LoadedChunkCount);

    public IWorld DefaultWorld { get; private set; } = default!;

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new BalancingTimer(20, stoppingToken);

        try
        {
            this.levelFactory.Initialize();

            await this.LoadWorldsAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync())
            {
                await Task.WhenAll(this.worlds.Values.Cast<World>().Select(x => x.ManageChunksAsync()));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await this.serverEnvironment.OnServerCrashAsync(ex);
        }

    }

    public async Task LoadWorldsAsync(CancellationToken cancellationToken = default)
    {
        var worlds = await LoadServerWorldsAsync(cancellationToken);
        foreach (var serverWorld in worlds)
        {
            var world = this.levelFactory.CreateWorld(serverWorld.Name, serverWorld.Seed, serverWorld.Generator);

            this.worlds.Add(world.Name, world);

            if (!CodecRegistry.TryGetDimension(serverWorld.DefaultDimension, out var defaultCodec) || !CodecRegistry.TryGetDimension("minecraft:overworld", out defaultCodec))
                throw new UnreachableException("Failed to get default dimension codec.");

            if (!await world.LoadAsync(defaultCodec))
            {
                this.logger.LogInformation("Creating new world: {worldName}...", serverWorld.Name);

                foreach (var dimensionName in serverWorld.ChildDimensions)
                {
                    if (!CodecRegistry.TryGetDimension(dimensionName, out var codec))
                    {
                        this.logger.LogWarning("Failed to find dimension with the name {dimensionName}", dimensionName);
                        continue;
                    }

                    var dimension = this.levelFactory.CreateDimension(world, codec.Name, dimensionName);

                    dimension.Initialize(codec);
                    world.RegisterDimension(codec, dimension);

                    await dimension.GenerateAsync();
                    await dimension.SaveAsync();
                }

                await world.GenerateAsync();
                await world.SaveAsync();
            }

            if (serverWorld.Default && this.DefaultWorld == null)
                this.DefaultWorld = world;

        }

        //No default world was defined so choose the first one to come up
        this.DefaultWorld ??= this.worlds.FirstOrDefault().Value;
        this.ReadyToJoin = true;
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

    public Task TickWorldsAsync() => Task.WhenAll(this.worlds.Values.Select(world => world.DoWorldTickAsync()));
    public Task FlushLoadedWorldsAsync() => Task.WhenAll(this.worlds.Values.Select(world => world.FlushRegionsAsync()));

    public async ValueTask DisposeAsync()
    {
        foreach (var world in this.worlds.Values)
        {
            await world.DisposeAsync();
        }

        this.serviceScope.Dispose();

        this.Dispose();
    }

    private static async Task<List<ServerWorld>> LoadServerWorldsAsync(CancellationToken cancellationToken = default)
    {
        var worldsFile = new FileInfo(Path.Combine("config", "worlds.json"));

        if (worldsFile.Exists)
        {
            await using var worldsFileStream = worldsFile.OpenRead();
            return await worldsFileStream.FromJsonAsync<List<ServerWorld>>(cancellationToken: cancellationToken)
                ?? throw new Exception("A worlds file does exist, but is invalid. Is it corrupt?");
        }

        var worlds = new List<ServerWorld>()
            {
                new()
                {
                    ChildDimensions =
                    {
                        "minecraft:the_nether",
                        "minecraft:the_end"
                    },
                    Seed = Globals.Random.Next().ToString()
                }
            };

        await using var fileStream = worldsFile.Create();
        await worlds.ToJsonAsync(fileStream, cancellationToken: cancellationToken);

        return worlds;
    }
}
