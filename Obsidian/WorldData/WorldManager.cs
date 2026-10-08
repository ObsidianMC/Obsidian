using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.API.Configuration;
using Obsidian.Hosting;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Obsidian.WorldData;

public sealed partial class WorldManager(ILogger<WorldManager> logger, IServiceProvider serviceProvider,
    IServerEnvironment serverEnvironment, ILevelFactory levelFactory, IHttpClientFactory httpClientFactory) : BackgroundService, IWorldManager
{
    private readonly ILogger<WorldManager> logger = logger;
    private readonly Dictionary<string, IWorld> worlds = [];
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

            // Structure templates come from Mojang's server jar, which Obsidian can't ship.
            StructureRegistry.Initialize(await VanillaServerJar.ExtractStructuresAsync(httpClientFactory.CreateClient(),
                ServerConstants.VanillaCachePath, ServerConstants.ProtocolDescription, this.logger, stoppingToken));

            await this.LoadWorldsAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync())
            {
                await Task.WhenAll(this.worlds.Values.Cast<World>()
                    .SelectMany(world => new[] { (AbstractLevel)world }.Concat(world.dimensions.Values.Cast<AbstractLevel>()))
                    .Select(level => level.ManageChunksAsync()));
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

            if (!CodecRegistry.TryGetDimension(serverWorld.DefaultDimension, out var defaultCodec) && !CodecRegistry.TryGetDimension("minecraft:overworld", out defaultCodec))
                throw new UnreachableException("Failed to get default dimension codec.");

            var loaded = await world.LoadAsync(defaultCodec);
            foreach (var dimensionName in serverWorld.ChildDimensions)
            {
                if (!CodecRegistry.TryGetDimension(dimensionName, out var codec))
                {
                    Log.UnknownDimension(this.logger, dimensionName, serverWorld.Name);
                    continue;
                }
                var dimension = this.levelFactory.CreateDimension(world, codec.Name, dimensionName);
                dimension.Initialize(codec);
                world.RegisterDimension(codec, dimension);
                if (!loaded)
                    await dimension.GenerateAsync();
            }

            if (!loaded)
            {
                Log.CreatingWorld(this.logger, serverWorld.Name);

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
    public Task FlushLoadedWorldsAsync() => Task.WhenAll(this.worlds.Values.Cast<World>().Select(world => world.FlushAsync()));

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

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Creating world {WorldName}")]
        public static partial void CreatingWorld(ILogger logger, string worldName);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping unknown dimension {DimensionName} in world {WorldName}")]
        public static partial void UnknownDimension(ILogger logger, string dimensionName, string worldName);
    }
}
