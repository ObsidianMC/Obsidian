using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.WorldData.Maps;
using System.IO;

namespace Obsidian.WorldData;

public sealed partial class World(ILogger<World> logger, IWorldManager worldManager, IPacketBroadcaster packetBroadcaster, IOptionsMonitor<ServerConfiguration> configuration,
    IEventDispatcher eventDispatcher, ILevelGenerator worldGenerator, string name, string seed) :
    AbstractLevel(logger, packetBroadcaster, configuration, eventDispatcher, worldGenerator, name, seed), IWorld
{
    public IWorldManager WorldManager { get; } = worldManager;

    internal Dictionary<string, IDimension> dimensions = [];

    public string PlayerDataPath { get; private set; } = string.Empty;

    /// <summary>
    /// The maps of the world and its dimensions.
    /// </summary>
    internal MapStorage Maps { get; private set; } = default!;

    // Saves and flushes of the world run one at a time: /save, autosaves and shutdown can overlap.
    private readonly System.Threading.SemaphoreSlim saveLock = new(1, 1);

    public async override Task<bool> LoadAsync(DimensionCodec codec)
    {
        this.Initialize(codec);

        // The level data is level.dat, or its backup (.old) when level.dat is missing or can't be read: a save that
        // stopped partway, or a damaged file. With neither, the world is new; with neither readable, it fails rather
        // than generate a new world over the damaged one.
        var backupPath = $"{this.LevelDataFilePath}.old";
        if (!File.Exists(this.LevelDataFilePath) && !File.Exists(backupPath))
            return false;

        var levelCompound = ReadLevelData(this.LevelDataFilePath, this.Logger) ?? ReadLevelData(backupPath, this.Logger)
            ?? throw new InvalidDataException($"Neither {this.LevelDataFilePath} nor its backup can be read.");
        LevelData = new LevelData()
        {
            Hardcore = levelCompound.GetBool("hardcore"),
            MapFeatures = levelCompound.GetBool("MapFeatures"),
            Raining = levelCompound.GetBool("raining"),
            Thundering = levelCompound.GetBool("thundering"),
            DefaultGamemode = (GameMode)levelCompound.GetInt("GameType"),
            GeneratorVersion = levelCompound.GetInt("generatorVersion"),
            RainTime = levelCompound.GetInt("rainTime"),
            // The spawn is saved as a block; players spawn at its center.
            SpawnPosition = new VectorD(levelCompound.GetInt("SpawnX") + 0.5, levelCompound.GetInt("SpawnY"), levelCompound.GetInt("SpawnZ") + 0.5),
            ThunderTime = levelCompound.GetInt("thunderTime"),
            Version = levelCompound.GetInt("version"),
            LastPlayed = levelCompound.GetLong("LastPlayed"),
            RandomSeed = levelCompound.GetLong("RandomSeed"),
            Time = levelCompound.GetLong("Time"),
            GeneratorName = levelCompound.GetString("generatorName"),
            LevelName = levelCompound.GetString("LevelName")
        };

        Log.Loading(this.Logger, this.Name);
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
            await this.SaveLevelAsync();
        }
        finally
        {
            this.saveLock.Release();
        }
    }

    /// <summary>
    /// Writes level.dat and the maps. The level data is written to a temporary file first and then replaces level.dat,
    /// keeping the previous one as level.dat.old, so a save that stops partway leaves a complete file.
    /// </summary>
    private async Task SaveLevelAsync()
    {
        var temporaryPath = $"{LevelDataFilePath}.tmp";
        await using (var fs = File.Create(temporaryPath))
            await this.WriteLevelDataAsync(fs);

        if (File.Exists(LevelDataFilePath))
            File.Replace(temporaryPath, LevelDataFilePath, $"{LevelDataFilePath}.old");
        else
            File.Move(temporaryPath, LevelDataFilePath);

        await this.Maps.SaveAsync();
    }

    private async Task WriteLevelDataAsync(Stream fs)
    {
        await using var writer = new NbtWriterStream(fs, NbtCompression.GZip, "");

        writer.WriteBool("hardcore", LevelData.Hardcore);
        writer.WriteBool("MapFeatures", LevelData.MapFeatures);
        writer.WriteBool("raining", LevelData.Raining);
        writer.WriteBool("thundering", LevelData.Thundering);
        writer.WriteInt("GameType", (int)LevelData.DefaultGamemode);
        writer.WriteInt("generatorVersion", LevelData.GeneratorVersion);
        writer.WriteInt("rainTime", LevelData.RainTime);
        var spawn = LevelData.SpawnPosition.Floor();
        writer.WriteInt("SpawnX", (int)spawn.X);
        writer.WriteInt("SpawnY", (int)spawn.Y);
        writer.WriteInt("SpawnZ", (int)spawn.Z);
        writer.WriteInt("thunderTime", LevelData.ThunderTime);
        writer.WriteInt("version", LevelData.Version);
        writer.WriteLong("LastPlayed", DateTimeOffset.Now.ToUnixTimeMilliseconds());
        writer.WriteLong("RandomSeed", LevelData.RandomSeed);
        writer.WriteLong("Time", Time);
        writer.WriteString("generatorName", Generator.Id);
        writer.WriteString("LevelName", Name);
        writer.EndCompound();

        await writer.TryFinishAsync();
    }

    /// <summary>
    /// Saves what changes while the world runs: its regions, then its level data and maps.
    /// </summary>
    public async Task FlushAsync()
    {
        await this.saveLock.WaitAsync();
        try
        {
            await this.FlushRegionsAsync();
            await Task.WhenAll(this.dimensions.Values.Select(dimension => dimension.FlushRegionsAsync()));
            await this.SaveLevelAsync();
        }
        finally
        {
            this.saveLock.Release();
        }
    }

    public async Task UnloadPlayerAsync(Guid uuid)
    {
        Players.TryRemove(uuid, out var player);
        await player.SaveAsync();
    }

    public void RegisterDimension(DimensionCodec codec, IDimension dimension)
    {
        if (dimensions.ContainsKey(codec.Name))
            throw new ArgumentException($"World already contains dimension with name: {codec.Name}");

        dimensions.Add(codec.Name, dimension);
    }

    public override void Initialize(DimensionCodec codec)
    {
        this.FolderPath = Path.Combine("worlds", Name);

        this.SetDimension(codec);

        this.LevelData = new LevelData
        {
            Time = codec.Element.FixedTime ?? 0,
            DefaultGamemode = GameMode.Survival,
            GeneratorName = Generator.Id
        };

        this.PlayerDataPath = Path.Combine(this.FolderPath, "playerdata");
        this.Maps = new MapStorage(this.FolderPath);
        this.LevelDataFilePath = Path.Combine(this.FolderPath, "level.dat");

        Directory.CreateDirectory(this.PlayerDataPath);
        Directory.CreateDirectory(this.FolderPath);
    }

    public async override Task DoWorldTickAsync()
    {
        await base.DoWorldTickAsync();

        await Task.WhenAll(this.dimensions.Values.Select(d => d.DoWorldTickAsync()));

        // Like vanilla's player inventory tick, after the levels ticked.
        foreach (var player in this.Players.Values.Concat(this.dimensions.Values.SelectMany(dimension => dimension.Players.Values)).Cast<Player>().ToArray())
        {
            await player.TickPortalsAsync();
            await player.PickupNearbyItemsAsync();
            await this.Maps.TickAsync(player);
        }
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
}
