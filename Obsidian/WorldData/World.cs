using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.WorldData.Maps;
using System.IO;

namespace Obsidian.WorldData;

public sealed partial class World(ILogger<World> logger, IWorldManager worldManager,
    IPacketBroadcaster packetBroadcaster, IOptionsMonitor<ServerConfiguration> configuration,
    IEventDispatcher eventDispatcher, ILevelGenerator worldGenerator, string name, string seed) :
    AbstractLevel(logger, packetBroadcaster, configuration, eventDispatcher, worldGenerator, name, seed), IWorld
{
    public IWorldManager WorldManager { get; } = worldManager;

    internal readonly ConcurrentDictionary<string, IDimension> dimensions = [];

    public string PlayerDataPath { get; private set; } = string.Empty;

    /// <summary>
    /// The maps of the world and its dimensions.
    /// </summary>
    internal MapStorage Maps { get; private set; } = default!;

    // Saves and flushes of the world run one at a time: /save, autosaves and shutdown can overlap.
    private readonly System.Threading.SemaphoreSlim saveLock = new(1, 1);

    // The folder UseFolder chose, or null for worlds/<name>.
    private string? folder;

    // The world's level data as it was read or created, written back with the fields Obsidian owns updated.
    private LevelFiles levelFiles = new();

    /// <summary>
    /// The world's folder, laid out like vanilla 26.3's saves (see <see cref="WorldLayout"/>): level.dat, the world's
    /// saved data, its players and maps, and its levels in <c>dimensions/</c>. <see cref="AbstractLevel.FolderPath"/>
    /// is the folder of the world's own level in it, like <c>dimensions/minecraft/overworld</c>.
    /// </summary>
    public string WorldFolderPath { get; private set; } = string.Empty;

    /// <summary>
    /// The singleplayer owner, whom vanilla's singleplayer game opens the world as (level.dat's
    /// <c>singleplayer_uuid</c>); set when the owner saves.
    /// </summary>
    internal Guid? SingleplayerOwner { get; set; }

    /// <summary>
    /// Puts the world in <paramref name="folder"/> instead of <c>worlds/&lt;name&gt;</c>. Call it before the world is
    /// loaded.
    /// </summary>
    internal void UseFolder(string folder) => this.folder = folder;

    /// <summary>
    /// Sets up the level data of a new world from <paramref name="files"/> (see <see cref="VanillaLevelData.Create"/>),
    /// which its level data files are then written from.
    /// </summary>
    internal void CreateLevel(LevelFiles files)
    {
        this.levelFiles = files;

        var level = VanillaLevelData.Read(files);
        level.GeneratorName = this.Generator.Id;
        level.Time = this.LevelData.Time;
        this.LevelData = level;

        // Before the dimensions are set up, which share the rules.
        ReadGameRules(files.GameRules);
    }

    public async override Task<bool> LoadAsync(DimensionCodec codec)
    {
        this.Initialize(codec);

        // With no level data, the world is new.
        if (LevelFiles.Load(this.WorldFolderPath, this.Logger) is not LevelFiles files)
            return false;

        this.levelFiles = files;
        this.LevelData = VanillaLevelData.Read(files);
        this.LevelData.GeneratorName = this.Generator.Id;
        this.SingleplayerOwner = VanillaLevelData.GetSingleplayerOwner(files);

        Log.Loading(this.Logger, this.Name);
        ReadGameRules(files.GameRules);
        this.ReadLevelState();
        this.ReadWanderingTrader(files.WanderingTrader);
        for (int rx = -1; rx < 1; rx++)
        {
            for (int rz = -1; rz < 1; rz++)
                LoadRegion(rx, rz);
        }

        var (x, z) = LevelData.SpawnPosition.ToChunkCoord();
        var index = 0;
        for (var cx = x - this.Configuration.SpawnChunkRadius; cx < x + this.Configuration.SpawnChunkRadius; cx++)
        {
            for (var cz = z - this.Configuration.SpawnChunkRadius; cz < z + this.Configuration.SpawnChunkRadius; cz++)
                this.spawnChunks[index++] = NumericsHelper.IntsToLong(cx, cz);
        }

        await Parallel.ForEachAsync(this.spawnChunks, async (c, _) =>
        {
            NumericsHelper.LongToInts(c, out var cx, out var cz);
            await GetChunkAsync(cx, cz);
        });

        Loaded = true;
        return true;
    }

    /// <summary>
    /// The level data at <paramref name="levelDataPath"/> or, when it's missing or can't be read (a save that stopped
    /// partway, a damaged file), its first readable backup: vanilla's <c>level.dat_old</c>, then Obsidian's
    /// <c>level.dat.old</c>. Whatever reads a world's level data uses this, so they all pick the same file.
    /// </summary>
    /// <returns>The root compound, or <c>null</c> when there's no level data at all, so the world is new.</returns>
    /// <exception cref="InvalidDataException">
    /// There is level data but none of it can be read; the world fails to load rather than be generated over it.
    /// </exception>
    internal static NbtCompound? ReadLevelDataOrBackup(string levelDataPath, ILogger logger)
    {
        string[] paths = [levelDataPath, $"{levelDataPath}_old", $"{levelDataPath}.old"];
        if (!paths.Any(File.Exists))
            return null;

        var readable = paths.Select(path => ReadLevelData(path, logger)).FirstOrDefault(compound => compound is not null);

        return readable ?? throw new InvalidDataException($"Neither {levelDataPath} nor its backup can be read.");
    }

    /// <summary>A level data file's root compound, or null when it's missing or can't be read.</summary>
    internal static NbtCompound? ReadLevelData(string path, ILogger logger)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var fs = File.OpenRead(path);
            return new NbtReader(fs, NbtCompression.GZip).ReadNextTag() as NbtCompound;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException
            or Obsidian.Nbt.Exceptions.NbtException or System.Diagnostics.UnreachableException)
        {
            Log.UnreadableLevelData(logger, path, ex);
            return null;
        }
    }

    // The world's save lock is held while saves wait for the levels' simulation gates (see FlushAsync), so a flow that
    // mustn't wait for those doesn't wait for it either: AbstractLevel.SaveAsync runs this in the background then.
    protected override async Task SaveLevelAsync()
    {
        await this.saveLock.WaitAsync();
        try
        {
            if (this.Disposed)
                return;

            this.SaveLevel();
        }
        finally
        {
            this.saveLock.Release();
        }
    }

    /// <summary>
    /// Writes the world's level data, its maps, and its and its dimensions' state (see
    /// <see cref="AbstractLevel.SaveLevelState"/>).
    /// </summary>
    private void SaveLevel()
    {
        VanillaLevelData.Update(this.levelFiles, this.LevelData);
        if (this.SingleplayerOwner is Guid owner)
            VanillaLevelData.SetSingleplayerOwner(this.levelFiles, owner);

        // Vanilla's wandering trader spawns in the overworld, which is the world's own level.
        this.WriteWanderingTrader(this.levelFiles.WanderingTrader);
        this.levelFiles.Save(this.WorldFolderPath);

        this.Maps.Save();

        this.SaveLevelState();
        foreach (var dimension in this.dimensions.Values.OfType<Dimension>())
            dimension.SaveLevelState();
    }

    /// <summary>
    /// Runs <paramref name="save"/> between the world's saves, which write the dimensions' files too.
    /// </summary>
    internal async Task SaveExclusiveAsync(Action save)
    {
        await this.saveLock.WaitAsync();
        try
        {
            save();
        }
        finally
        {
            this.saveLock.Release();
        }
    }

    /// <summary>
    /// Saves what changes while the world runs: its and its dimensions' regions, then its level data and maps.
    /// </summary>
    /// <remarks>
    /// It waits for the levels' simulation gates, so called from inside a level's tick, or a player's tick, grant or
    /// save, it saves in the background once the ticks let go of them, and returns at once; a failure is then logged.
    /// </remarks>
    public Task FlushAsync() => RunOutsideTicksAsync(this.FlushWorldAsync, this.DeferredWork);

    private async Task FlushWorldAsync()
    {
        await this.saveLock.WaitAsync();
        try
        {
            // A flush that ran late, once the world was disposed, writes nothing (see AbstractLevel.SaveAsync).
            if (this.Disposed)
                return;

            await this.FlushRegionsAsync();
            this.SaveLevel();
        }
        finally
        {
            this.saveLock.Release();
        }
    }

    public async Task UnloadPlayerAsync(Guid uuid)
    {
        if (Players.TryRemove(uuid, out var player))
        {
            API.Data.AttachmentStorage.For(player).Retire();
            await player.SaveAsync();
        }
    }

    /// <summary>
    /// Sets the difficulty, whether it's locked, or both, in the world and its dimensions: like vanilla's levels, they
    /// share one difficulty. Changes run one at a time, so concurrent ones never leave the levels apart.
    /// </summary>
    /// <param name="difficulty">The new difficulty, or <c>null</c> to keep it.</param>
    /// <param name="locked">Whether the difficulty is locked, or <c>null</c> to keep that.</param>
    /// <param name="force">
    /// Whether a locked difficulty changes too, like vanilla's <c>MinecraftServer.setDifficulty</c>: a command forces
    /// it, a player's request doesn't.
    /// </param>
    /// <returns>Whether it changed: <c>false</c> when the difficulty is locked and the change isn't forced.</returns>
    internal bool SetDifficulty(Difficulty? difficulty = null, bool? locked = null, bool force = true)
    {
        lock (this.difficultyGate)
        {
            if (!force && this.LevelData.DifficultyLocked)
                return false;

            var newDifficulty = difficulty ?? this.LevelData.Difficulty;
            var newLocked = locked ?? this.LevelData.DifficultyLocked;
            foreach (var level in this.dimensions.Values.OfType<AbstractLevel>().Prepend(this))
            {
                level.LevelData.Difficulty = newDifficulty;
                level.LevelData.DifficultyLocked = newLocked;
            }

            return true;
        }
    }

    // Serializes difficulty changes (see SetDifficulty).
    private readonly System.Threading.Lock difficultyGate = new();

    public ILevel? GetLevel(string identifier) =>
        this.DimensionName == identifier ? this : this.dimensions.GetValueOrDefault(identifier);

    public void RegisterDimension(IDimension dimension)
    {
        if (!dimensions.TryAdd(dimension.DimensionName, dimension))
            throw new ArgumentException($"World already contains dimension with name: {dimension.DimensionName}");
    }

    public override void Initialize(DimensionCodec codec)
    {
        this.WorldFolderPath = this.folder ?? Path.Combine(ServerConstants.WorldsPath, Name);

        this.SetDimension(codec.Name, codec);
        this.FolderPath = Path.Combine(this.WorldFolderPath, WorldLayout.LevelFolder(codec.Name));

        this.LevelData = new LevelData
        {
            Time = codec.Element.FixedTime ?? 0,
            DefaultGamemode = GameMode.Survival,
            GeneratorName = Generator.Id
        };

        this.PlayerDataPath = Path.Combine(this.WorldFolderPath, WorldLayout.PlayerDataFolder);
        this.Maps = new MapStorage(this.WorldFolderPath);
        this.LevelDataFilePath = Path.Combine(this.WorldFolderPath, WorldLayout.LevelDataFile);

        Directory.CreateDirectory(this.PlayerDataPath);
        Directory.CreateDirectory(this.FolderPath);
    }

    /// <summary>
    /// Ticks the world: its clocks, then the world's own level and its dimensions, then its players once every level is
    /// done.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <see cref="ServerConfiguration.TicksLevelsInParallel"/> on, every level's tick runs at the same time as the
    /// others, each started on a thread of its own; turned off, the world's own level ticks first and its dimensions
    /// after it. Either way no level's tick waits for a lock another level's tick holds: what they share (the clocks,
    /// game rules and difficulty, the server's players, scoreboards and boss bars, the maps and saved data) is safe to
    /// read and change from several threads, travel between them is a handoff that holds one level's lock at a time
    /// (see <see cref="EntityTransferLock"/>), and a tick never waits for a level's simulation gate (see
    /// <see cref="AbstractLevel.RunOutsideTicksAsync"/>).
    /// </para>
    /// <para>
    /// The clocks only advance here, before any level ticks. The players' pass after the levels must wait for every
    /// one of them, since a player can have moved between levels during the tick.
    /// </para>
    /// </remarks>
    public async override Task DoWorldTickAsync()
    {
        ThrowIfMustNotWaitForGates(nameof(DoWorldTickAsync));
        this.TickClocks();

        if (this.Configuration.TicksLevelsInParallel)
            await this.TickLevelsInParallelAsync();
        else
            await this.TickLevelsInOrderAsync();

        // Like vanilla's player inventory tick, after the levels ticked.
        var players = this.Players.Values
            .Concat(this.dimensions.Values.SelectMany(dimension => dimension.Players.Values))
            .ToArray();

        foreach (var player in players)
            await player.TickAfterLevelsAsync();
    }

    // Each level's tick starts on a thread of its own: started inline, a tick would run on the caller's thread until it
    // first awaited something unfinished, and the levels would mostly tick one after another.
    private Task TickLevelsInParallelAsync()
    {
        var ticks = new List<Task>(this.dimensions.Count + 1)
        {
            Task.Run(this.TickOwnLevelAsync)
        };

        foreach (var dimension in this.dimensions.Values)
            ticks.Add(Task.Run(dimension.DoWorldTickAsync));

        return Task.WhenAll(ticks);
    }

    private async Task TickLevelsInOrderAsync()
    {
        await this.TickOwnLevelAsync();
        await Task.WhenAll(this.dimensions.Values.Select(dimension => dimension.DoWorldTickAsync()));
    }

    private Task TickOwnLevelAsync() => base.DoWorldTickAsync();

    // Like vanilla's server, which ticks its clocks once before any level ticks: the world's levels share its clocks,
    // so their passing time never changes under a level's tick. Game time advances with them, as the overworld's level
    // data keeps it for every level.
    private void TickClocks()
    {
        if (this.LevelData is null)
            return;

        var advanceTime = this.LevelData.GetBooleanRule("advance_time");
        this.LevelData.WorldClocks.Tick(advanceTime, this.Configuration.TimeTickSpeedMultiplier);
    }

    public override async ValueTask DisposeAsync()
    {
        // Saves asked for from the ticks of the world, its dimensions and its players run first, and none after.
        await this.DeferredWork.CloseAndDrainAsync();

        // The world's own level is disposed even when a dimension's disposal fails, and every level's failure is the
        // disposal's, so a shutdown reports each level it couldn't save.
        List<Exception> failures = [];
        var dimensions = Task.WhenAll(this.dimensions.Values.Select(dimension => dimension.DisposeAsync().AsTask()));
        try
        {
            await dimensions;
        }
        catch (Exception exception)
        {
            failures.AddRange(dimensions.Exception?.InnerExceptions ?? [exception]);
        }

        try
        {
            await base.DisposeAsync();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failures[0]);

        if (failures.Count > 1)
            throw new AggregateException(failures);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Loading world {WorldName}")]
        public static partial void Loading(ILogger logger, string worldName);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Can't read the level data in {Path}")]
        public static partial void UnreadableLevelData(ILogger logger, string path, Exception exception);
    }

    protected async override Task FlushLevelRegionsAsync()
    {
        await base.FlushLevelRegionsAsync();
        await Task.WhenAll(dimensions.Values.Select(dimension => dimension.FlushRegionsAsync()));
    }
}
