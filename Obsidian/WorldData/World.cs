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
            for (int rz = -1; rz < 1; rz++)
                LoadRegion(rx, rz);

        var (x, z) = LevelData.SpawnPosition.ToChunkCoord();
        var index = 0;
        for (var cx = x - this.Configuration.SpawnChunkRadius; cx < x + this.Configuration.SpawnChunkRadius; cx++)
            for (var cz = z - this.Configuration.SpawnChunkRadius; cz < z + this.Configuration.SpawnChunkRadius; cz++)
                this.spawnChunks[index++] = NumericsHelper.IntsToLong(cx, cz);

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

    public override async Task SaveAsync()
    {
        await this.saveLock.WaitAsync();
        try
        {
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
    public async Task FlushAsync()
    {
        await this.saveLock.WaitAsync();
        try
        {
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
    /// Sets the difficulty and whether it's locked, in the world and its dimensions: like vanilla's levels, they share
    /// one difficulty.
    /// </summary>
    internal void SetDifficulty(Difficulty difficulty, bool locked)
    {
        foreach (var level in this.dimensions.Values.OfType<AbstractLevel>().Prepend(this))
        {
            level.LevelData.Difficulty = difficulty;
            level.LevelData.DifficultyLocked = locked;
        }
    }

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

    public async override Task DoWorldTickAsync()
    {
        await base.DoWorldTickAsync();

        await Task.WhenAll(this.dimensions.Values.Select(d => d.DoWorldTickAsync()));

        // Like vanilla's player inventory tick, after the levels ticked.
        foreach (var player in this.Players.Values.Concat(this.dimensions.Values.SelectMany(dimension => dimension.Players.Values)).ToArray())
            await player.TickAfterLevelsAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await Task.WhenAll(this.dimensions.Values.Select(dimension => dimension.DisposeAsync().AsTask()));
        await base.DisposeAsync();
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Loading world {WorldName}")]
        public static partial void Loading(ILogger logger, string worldName);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Can't read the level data in {Path}")]
        public static partial void UnreadableLevelData(ILogger logger, string path, Exception exception);
    }

    public async override Task FlushRegionsAsync()
    {
        await base.FlushRegionsAsync();
        await Task.WhenAll(dimensions.Values.Select(dimension => dimension.FlushRegionsAsync()));
    }
}
