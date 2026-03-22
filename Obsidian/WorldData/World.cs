using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Nbt;
using System.IO;

namespace Obsidian.WorldData;

public sealed class World(ILogger<World> logger, IWorldManager worldManager, IPacketBroadcaster packetBroadcaster, IOptionsMonitor<ServerConfiguration> configuration,
    IEventDispatcher eventDispatcher, ILevelGenerator worldGenerator, string name, string seed) : 
    AbstractLevel(logger, packetBroadcaster, configuration.CurrentValue, eventDispatcher, worldGenerator, name, seed), IWorld
{
    private const int SpawnChunkRadius = 12;

    public IWorldManager WorldManager { get; } = worldManager;

    internal Dictionary<string, IDimension> dimensions = [];

    public string PlayerDataPath { get; private set; } = string.Empty;

    public async override Task<bool> LoadAsync(DimensionCodec codec)
    {
        var fi = new FileInfo(this.LevelDataFilePath);
        if (!fi.Exists)
            return false;

        var reader = new NbtReader(fi.OpenRead(), NbtCompression.GZip);
        var levelCompound = (reader.ReadNextTag() as NbtCompound)!;
        LevelData = new Level()
        {
            Hardcore = levelCompound.GetBool("hardcore"),
            MapFeatures = levelCompound.GetBool("MapFeatures"),
            Raining = levelCompound.GetBool("raining"),
            Thundering = levelCompound.GetBool("thundering"),
            DefaultGamemode = (Gamemode)levelCompound.GetInt("GameType"),
            GeneratorVersion = levelCompound.GetInt("generatorVersion"),
            RainTime = levelCompound.GetInt("rainTime"),
            SpawnPosition = new VectorF(levelCompound.GetInt("SpawnX"), levelCompound.GetInt("SpawnY"), levelCompound.GetInt("SpawnZ")),
            ThunderTime = levelCompound.GetInt("thunderTime"),
            Version = levelCompound.GetInt("version"),
            LastPlayed = levelCompound.GetLong("LastPlayed"),
            RandomSeed = levelCompound.GetLong("RandomSeed"),
            Time = levelCompound.GetLong("Time"),
            GeneratorName = levelCompound.GetString("generatorName"),
            LevelName = levelCompound.GetString("LevelName")
        };

        Logger.LogInformation("Loading spawn chunks into memory...");
        for (int rx = -1; rx < 1; rx++)
            for (int rz = -1; rz < 1; rz++)
                LoadRegion(rx, rz);

        var (x, z) = LevelData.SpawnPosition.ToChunkCoord();
        var index = 0;
        for (var cx = x - SpawnChunkRadius; cx < x + SpawnChunkRadius; cx++)
            for (var cz = z - SpawnChunkRadius; cz < z + SpawnChunkRadius; cz++)
                SpawnChunks[index++] = NumericsHelper.IntsToLong(cx, cz);

        await Parallel.ForEachAsync(SpawnChunks, async (c, _) =>
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
        writer.WriteInt("SpawnX", (int)LevelData.SpawnPosition.X);
        writer.WriteInt("SpawnY", (int)LevelData.SpawnPosition.Y);
        writer.WriteInt("SpawnZ", (int)LevelData.SpawnPosition.Z);
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

        this.DimensionName = codec.Name;

        this.LevelData = new Level
        {
            Time = codec.Element.FixedTime ?? 0,
            DefaultGamemode = Gamemode.Survival,
            GeneratorName = Generator.Id
        };

        this.PlayerDataPath = Path.Combine(this.FolderPath, "playerdata");
        this.LevelDataFilePath = Path.Combine(this.FolderPath, "level.dat");

        Directory.CreateDirectory(this.PlayerDataPath);
        Directory.CreateDirectory(this.FolderPath);
    }
}
