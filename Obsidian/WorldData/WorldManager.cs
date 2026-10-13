using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.Hosting;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace Obsidian.WorldData;

public sealed partial class WorldManager(ILogger<WorldManager> logger, IServiceProvider serviceProvider,
    IServerEnvironment serverEnvironment, ILevelFactory levelFactory, IHttpClientFactory httpClientFactory,
    IOptions<IntegratedConfiguration> integratedOptions, IOptions<NewWorldConfiguration> newWorldOptions) : BackgroundService, IWorldManager
{
    private static readonly string[] VanillaDimensions = ["minecraft:the_nether", "minecraft:the_end"];

    private readonly ILogger<WorldManager> logger = logger;
    private readonly ConcurrentDictionary<string, IWorld> worlds = [];
    private readonly IServerEnvironment serverEnvironment = serverEnvironment;
    private readonly ILevelFactory levelFactory = levelFactory;
    private readonly IServiceScope serviceScope = serviceProvider.CreateScope();

    // Plugins' dimensions are added one pass at a time, so none is created twice.
    private readonly SemaphoreSlim pluginDimensionLock = new(1, 1);

    // Whether loading the worlds started adding plugins' dimensions, after which every pass adds them; set under
    // pluginDimensionLock.
    private bool pluginDimensionsOpen;

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
            // Plugins register world generators and content, which the worlds need, so they load first.
            if (serviceProvider.GetService<Plugins.PluginStartup>() is Plugins.PluginStartup pluginStartup)
                await pluginStartup.Loaded.WaitAsync(stoppingToken);

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

        await this.AddPluginDimensionsAsync(opening: true, cancellationToken);
        if (this.worlds.Values.Any(world => world is AbstractLevel { Generator: Generators.MobTestGenerator }))
        {
            var commands = this.serviceScope.ServiceProvider.GetRequiredService<Obsidian.Commands.Framework.CommandHandler>();
            if (!commands.GetAllCommands().Any(command => command.Name == "mob_tp"))
                commands.RegisterCommandClass<Obsidian.Commands.Modules.MobTestCommandModule>(null);
        }

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
        if (!this.worlds.TryAdd(world.Name, world))
            throw new InvalidOperationException($"World already exists: {world.Name}");

        if (!CodecRegistry.TryGetDimension(serverWorld.DefaultDimension, out var defaultCodec)
            && !CodecRegistry.TryGetDimension("minecraft:overworld", out defaultCodec))
            throw new UnreachableException("Failed to get default dimension codec.");

        var loaded = await world.LoadAsync(defaultCodec);
        if (!loaded)
        {
            ThrowIfCantGenerate(world);
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
            world.RegisterDimension(dimension);

            // A dimension saved before reads its own level data, if it saved any, and its chunks load as they're
            // needed. One that wasn't is new, even in a loaded world, and is generated with its spawn.
            if (dimension is Dimension { IsSaved: true })
            {
                await dimension.LoadAsync(codec);
            }
            else
            {
                ThrowIfCantGenerate(dimension);

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

    // A saved level whose plugin generator is missing loads stopped, but a new one would be created without its
    // spawn area, so it isn't created at all.
    private static void ThrowIfCantGenerate(ILevel level)
    {
        if (level is AbstractLevel { Generator: PluginLevelGenerator { Plugin: null } generator })
        {
            throw new InvalidOperationException(
                $"{level.Name} can't be created: no generator {generator.Id} is registered.");
        }
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
            generatorId = VanillaLevelData.GetGeneratorId(data, PluginDimensions.FindStem("minecraft:overworld"));
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

    /// <summary>
    /// Creates the levels of plugins' dimensions (see <see cref="PluginDimensions"/>) the default world doesn't have
    /// yet, loading the saved ones and generating the others. Their levels stay until the server stops.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the default world has plugins' dimensions, like vanilla's single world has a data pack's. A level saved by
    /// a plugin that isn't loaded any more isn't loaded, and its folder is left alone.
    /// </para>
    /// <para>
    /// Until the worlds load, this does nothing: loading them adds the dimensions plugins added by then. A plugin
    /// loading while they're added waits for that pass, then adds those it missed.
    /// </para>
    /// </remarks>
    internal Task AddPluginDimensionsAsync(CancellationToken cancellationToken = default) =>
        this.AddPluginDimensionsAsync(opening: false, cancellationToken);

    // A pass adding plugins' dimensions, the first of which, opening, runs as the worlds load. As passes run one at a
    // time, one a plugin asked for before it, finding them not open, has its dimensions added by that first pass.
    private async Task AddPluginDimensionsAsync(bool opening, CancellationToken cancellationToken)
    {
        await this.pluginDimensionLock.WaitAsync(cancellationToken);

        var pass = new PluginManager.DimensionPass();

        try
        {
            // Generating the dimensions' spawn chunks runs plugins' code, which can't change plugins from here.
            PluginManager.creatingDimensions.Value = pass;
            this.pluginDimensionsOpen |= opening;

            if (!this.pluginDimensionsOpen || this.DefaultWorld is not World world)
                return;

            foreach (var stem in PluginDimensions.Added)
            {
                // A released dimension can't generate until its plugin loads again.
                if (stem.IsReleased || world.dimensions.ContainsKey(stem.Identifier))
                    continue;

                await this.AddPluginDimensionAsync(world, stem, cancellationToken);
            }
        }
        finally
        {
            pass.End();
            PluginManager.creatingDimensions.Value = null;
            this.pluginDimensionLock.Release();
        }
    }

    private async Task AddPluginDimensionAsync(World world, LevelStem stem, CancellationToken cancellationToken)
    {
        var factory = (LevelFactory)this.levelFactory;
        IDimension? dimension = null;
        var registered = false;

        // The level is registered once it's loaded or generated with its spawn, so players can't reach it, and it
        // doesn't tick, before then. One that fails is left out and disposed; a later plugin load tries it again.
        try
        {
            // Building a noise generator orders the features of the dimension's biomes, which fails when they can't
            // be ordered, as vanilla's would.
            var generator = stem.CreateGenerator();
            dimension = factory.CreateDimension(world, stem.Identifier, generator);

            // The stem is a snapshot: a plugin may have unloaded, or a reload changed the files, since. The level
            // generates with the dimension it took as it was created, or is left out when that one went; a later
            // plugin load tries it again.
            if (generator.Taken is not LevelStem taken)
                return;

            var type = taken.DimensionType;
            dimension.Initialize(type);

            if (!await dimension.LoadAsync(type))
            {
                Log.CreatingDimension(this.logger, stem.Identifier, world.Name);
                await dimension.GenerateAsync(cancellationToken);
                await dimension.SaveAsync();
            }

            world.RegisterDimension(dimension);
            registered = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.PluginDimensionFailed(this.logger, ex, stem.Identifier);
        }
        finally
        {
            if (!registered && dimension is not null)
                await this.DisposeFailedDimensionAsync(dimension, stem.Identifier);
        }
    }

    private async Task DisposeFailedDimensionAsync(IDimension dimension, string identifier)
    {
        try
        {
            await dimension.DisposeAsync();
        }
        catch (Exception ex)
        {
            Log.PluginDimensionFailed(this.logger, ex, identifier);
        }
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

    public Task TickWorldsAsync() => ReadyToJoin ? Task.WhenAll(this.worlds.Values.Select(world => world.DoWorldTickAsync())) : Task.CompletedTask;
    public Task FlushLoadedWorldsAsync() => Task.WhenAll(this.worlds.Values.Cast<World>().Select(world => world.FlushAsync()));

    public async ValueTask DisposeAsync()
    {
        // Loading, generation and chunk management stop first, so nothing generates into the disposed levels. The levels
        // then wait for the chunks still generating. The server saved the worlds already, unless they hadn't loaded.
        await this.StopAsync(CancellationToken.None);

        // A world disposes its dimensions too.
        foreach (var world in this.worlds.Values)
            await world.DisposeAsync();

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
            var savedWorlds = await worldsFileStream.FromJsonAsync<List<ServerWorld>>(cancellationToken: cancellationToken)
                ?? throw new Exception("A worlds file does exist, but is invalid. Is it corrupt?");
            return ApplyWorldEnvironmentOverrides(savedWorlds);
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

        return ApplyWorldEnvironmentOverrides(worlds);
    }

    private static List<ServerWorld> ApplyWorldEnvironmentOverrides(List<ServerWorld> worlds)
    {
        // Merge before binding so list entries are overridden by index rather than appended.
        using var defaults = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new { Worlds = worlds }));
        using var configuration = new ConfigurationManager();
        configuration.AddJsonStream(defaults).AddEnvironmentVariables();
        return configuration.GetSection("Worlds").Get<List<ServerWorld>>() ?? [];
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

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Creating dimension {DimensionName} in world {WorldName}")]
        public static partial void CreatingDimension(ILogger logger, string dimensionName, string worldName);

        [LoggerMessage(Level = LogLevel.Error, Message = "Can't generate dimension {DimensionName}; it's left out")]
        public static partial void PluginDimensionFailed(ILogger logger, Exception exception, string dimensionName);
    }
}
