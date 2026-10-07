using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.Hosting;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Obsidian.WorldData;

public sealed partial class WorldManager(ILogger<WorldManager> logger, IServiceProvider serviceProvider,
    IServerEnvironment serverEnvironment, ILevelFactory levelFactory, IHttpClientFactory httpClientFactory,
    IOptions<IntegratedConfiguration> integratedOptions, IOptions<NewWorldConfiguration> newWorldOptions) : BackgroundService, IWorldManager
{
    private static readonly string[] VanillaDimensions = ["minecraft:the_nether", "minecraft:the_end"];

    private readonly ILogger<WorldManager> logger = logger;
    private readonly Dictionary<string, IWorld> worlds = [];
    private readonly IServerEnvironment serverEnvironment = serverEnvironment;
    private readonly ILevelFactory levelFactory = levelFactory;
    private readonly IServiceScope serviceScope = serviceProvider.CreateScope();

    // The lock on the single world's folder (see LoadSingleWorldAsync), held until the worlds are disposed.
    private SessionLock? sessionLock;

    public bool ReadyToJoin { get; private set; }

    /// <summary>
    /// How far loading the worlds got, for an integrated server's loading screen: <c>preparing</c> while existing worlds
    /// load, <c>generating</c> while new ones generate, with a percentage.
    /// </summary>
    public LoadProgress Progress { get; private set; } = new("preparing", 0);

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
                // Every level manages its chunks, the dimensions too: it's what generates the chunks players ask for.
                var levels = this.worlds.Values
                    .Cast<World>()
                    .SelectMany(world => world.dimensions.Values.Cast<AbstractLevel>().Prepend(world));

                await Task.WhenAll(levels.Select(level => level.ManageChunksAsync()));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping, possibly while the worlds were still loading or generating.
        }
        catch (Exception ex)
        {
            await this.serverEnvironment.OnServerCrashAsync(ex);
        }
    }

    public async Task LoadWorldsAsync(CancellationToken cancellationToken = default)
    {
        var integrated = integratedOptions.Value;
        if (integrated.Enabled && !string.IsNullOrWhiteSpace(integrated.WorldPath))
        {
            await this.LoadSingleWorldAsync(Path.GetFullPath(integrated.WorldPath), cancellationToken);
        }
        else
        {
            foreach (var serverWorld in await LoadServerWorldsAsync(cancellationToken))
            {
                var world = this.levelFactory.CreateWorld(serverWorld.Name, serverWorld.Seed, serverWorld.Generator);
                await this.LoadWorldAsync((World)world, serverWorld, cancellationToken: cancellationToken);
            }
        }

        //No default world was defined so choose the first one to come up
        this.DefaultWorld ??= this.worlds.FirstOrDefault().Value;
        this.Progress = new("generating", 100);
        this.ReadyToJoin = true;
    }

    /// <summary>
    /// Loads a world, or generates it with its dimensions when it's new.
    /// </summary>
    /// <param name="createLevel">Sets up a new world's level data before it's generated.</param>
    /// <param name="cancellationToken">
    /// Stops generating a new world, throwing <see cref="OperationCanceledException"/> before its level data is saved.
    /// </param>
    private async Task LoadWorldAsync(World world, ServerWorld serverWorld, Action<World>? createLevel = null,
        CancellationToken cancellationToken = default)
    {
        this.worlds.Add(world.Name, world);

        if (!CodecRegistry.TryGetDimension(serverWorld.DefaultDimension, out var defaultCodec)
            && !CodecRegistry.TryGetDimension("minecraft:overworld", out defaultCodec))
            throw new UnreachableException("Failed to get default dimension codec.");

        var loaded = await world.LoadAsync(defaultCodec);
        if (!loaded)
        {
            Log.CreatingWorld(this.logger, serverWorld.Name);
            createLevel?.Invoke(world);
        }

        // An integrated server reports generation as progress events instead of printing it. The levels generate one
        // after another, each an equal share of the progress.
        var levels = serverWorld.ChildDimensions.Count + 1;
        var generated = 0;
        void Report(int done, int total)
        {
            var levelPercent = done * 100 / Math.Max(total, 1);
            this.Progress = new("generating", (generated * 100 + levelPercent) / levels);
        }

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

            // A loaded world's dimensions are already generated; their chunks load as they're needed.
            if (!loaded)
            {
                if (integratedOptions.Value.Enabled)
                    ((AbstractLevel)dimension).GenerationProgress = Report;

                await dimension.GenerateAsync(cancellationToken);
                await dimension.SaveAsync();
            }

            generated++;
        }

        if (!loaded)
        {
            if (integratedOptions.Value.Enabled)
                world.GenerationProgress = Report;

            await world.GenerateAsync(cancellationToken);
            await world.SaveAsync();
        }

        if (serverWorld.Default && this.DefaultWorld is null)
            this.DefaultWorld = world;
    }

    /// <summary>
    /// Loads the one world of an integrated server: a vanilla save in <paramref name="folder"/>, which is created from the
    /// <c>NewWorld</c> settings when it has no level.dat. The folder stays locked while the world is open.
    /// </summary>
    /// <exception cref="NotSupportedException">Obsidian can't generate the world.</exception>
    private async Task LoadSingleWorldAsync(string folder, CancellationToken cancellationToken)
    {
        this.sessionLock = SessionLock.Acquire(folder);

        var settings = newWorldOptions.Value;
        var levelDataPath = Path.Combine(folder, "level.dat");
        var root = World.ReadLevelDataOrBackup(levelDataPath, this.logger);
        var data = root is not null ? VanillaLevelData.GetData(root) : null;

        string name, generatorId;
        long seed;
        if (data is not null)
        {
            var hasName = data.TryGetTagValue<string>("LevelName", out var levelName) && !string.IsNullOrEmpty(levelName);
            name = hasName ? levelName! : Path.GetFileName(folder);
            generatorId = VanillaLevelData.GetGeneratorId(data);
            seed = VanillaLevelData.Read(data).RandomSeed;
        }
        else if (root is not null)
        {
            throw new NotSupportedException($"{levelDataPath} isn't a vanilla level.dat.");
        }
        else
        {
            name = string.IsNullOrWhiteSpace(settings.LevelName) ? Path.GetFileName(folder) : settings.LevelName;
            generatorId = VanillaLevelData.GetGeneratorId(settings.WorldType);
            seed = VanillaLevelData.ParseSeed(settings.Seed);
        }

        var serverWorld = new ServerWorld
        {
            Name = name,
            Generator = generatorId,
            Seed = seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Default = true,
            ChildDimensions = [.. VanillaDimensions]
        };

        var world = (World)this.levelFactory.CreateWorld(serverWorld.Name, serverWorld.Seed, serverWorld.Generator);
        world.UseVanillaLayout(folder);

        await this.LoadWorldAsync(world, serverWorld,
            createLevel: newWorld => newWorld.CreateVanillaLevel(VanillaLevelData.Create(settings, name, seed, generatorId)),
            cancellationToken);
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
        // Loading, generation and chunk management stop first, so nothing generates into the disposed levels. The levels
        // then wait for the chunks still generating.
        await this.StopAsync(CancellationToken.None);

        foreach (var world in this.worlds.Values.Cast<World>())
        {
            foreach (var dimension in world.dimensions.Values)
                await dimension.DisposeAsync();

            await world.DisposeAsync();
        }

        this.sessionLock?.Dispose();
        this.serviceScope.Dispose();

        this.Dispose();
    }

    private static async Task<List<ServerWorld>> LoadServerWorldsAsync(CancellationToken cancellationToken = default)
    {
        var worldsFile = new FileInfo(Path.Combine(ServerConstants.ConfigPath, "worlds.json"));

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
                    ChildDimensions = [.. VanillaDimensions],
                    Seed = Globals.Random.Next().ToString()
                }
            };

        worldsFile.Directory?.Create();
        await using var fileStream = worldsFile.Create();
        await worlds.ToJsonAsync(fileStream, cancellationToken: cancellationToken);

        return worlds;
    }

    /// <summary>
    /// A loading stage (<c>preparing</c> or <c>generating</c>) and how far it got, from 0 to 100.
    /// </summary>
    public sealed record LoadProgress(string Stage, int Percent);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Creating world {WorldName}")]
        public static partial void CreatingWorld(ILogger logger, string worldName);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping unknown dimension {DimensionName} in world {WorldName}")]
        public static partial void UnknownDimension(ILogger logger, string dimensionName, string worldName);
    }
}
