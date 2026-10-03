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

    public async override Task<bool> LoadAsync(DimensionCodec codec)
    {
        this.Initialize(codec);

        var fi = new FileInfo(this.LevelDataFilePath);
        if (!fi.Exists)
            return false;


        await using var fs = fi.OpenRead();
        var reader = new NbtReader(fs, NbtCompression.GZip);
        var levelCompound = (reader.ReadNextTag() as NbtCompound)!;
        LevelData = new LevelData()
        {
            Hardcore = levelCompound.GetBool("hardcore"),
            MapFeatures = levelCompound.GetBool("MapFeatures"),
            Raining = levelCompound.GetBool("raining"),
            Thundering = levelCompound.GetBool("thundering"),
            DefaultGamemode = (Gamemode)levelCompound.GetInt("GameType"),
            GeneratorVersion = levelCompound.GetInt("generatorVersion"),
            RainTime = levelCompound.GetInt("rainTime"),
            // The spawn is saved as a block; players spawn at its center.
            SpawnPosition = new VectorF(levelCompound.GetInt("SpawnX") + 0.5f, levelCompound.GetInt("SpawnY"), levelCompound.GetInt("SpawnZ") + 0.5f),
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

    public override async Task SaveAsync()
    {
        var worldFile = new FileInfo(LevelDataFilePath);

        if (worldFile.Exists)
        {
            worldFile.CopyTo($"{LevelDataFilePath}.old", true);
            worldFile.Delete();
        }

        await using var fs = worldFile.Create();
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

        await this.Maps.SaveAsync();
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
            DefaultGamemode = Gamemode.Survival,
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
        foreach (var player in this.Players.Values.Concat(this.dimensions.Values.SelectMany(dimension => dimension.Players.Values)).Cast<Player>())
            await this.Maps.TickAsync(player);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Loading world {WorldName}")]
        public static partial void Loading(ILogger logger, string worldName);
    }
}
