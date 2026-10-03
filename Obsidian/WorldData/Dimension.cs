using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.API.Registry.Codecs.Dimensions;
using System.IO;
using Obsidian.Nbt;

namespace Obsidian.WorldData;

internal sealed class Dimension(ILogger<Dimension> logger, IPacketBroadcaster packetBroadcaster, IOptionsMonitor<ServerConfiguration> configuration,
    IEventDispatcher eventDispatcher, ILevelGenerator worldGenerator, string name, IWorld world) :
    AbstractLevel(logger, packetBroadcaster, configuration, eventDispatcher, worldGenerator, name, world.Seed), IDimension
{
    public IWorld ParentWorld { get; } = world;

    public override void Initialize(DimensionCodec codec)
    {
        this.FolderPath = Path.Combine("worlds", ParentWorld.Name, "dimensions", this.Name.TrimResourceTag(true));

        this.SetDimension(codec);

        this.LevelData = new LevelData
        {
            Time = codec.Element.FixedTime ?? 0,
            DefaultGamemode = Gamemode.Survival,
            GeneratorName = Generator.Id
        };

        this.LevelDataFilePath = Path.Combine(this.FolderPath, "level.dat");

        Directory.CreateDirectory(this.FolderPath);
    }

    public override async Task<bool> LoadAsync(DimensionCodec codec)
    {
        if (!File.Exists(LevelDataFilePath)) return false;
        await using var stream = File.OpenRead(LevelDataFilePath);
        var reader = new NbtReader(stream, NbtCompression.GZip);
        if (reader.ReadNextTag() is not NbtCompound data) return false;
        if (data.TryGetTagValue<int>("SpawnX", out var x) && data.TryGetTagValue<int>("SpawnY", out var y) && data.TryGetTagValue<int>("SpawnZ", out var z))
            LevelData.SpawnPosition = new VectorF(x + 0.5f, y, z + 0.5f);
        if (data.TryGetTagValue<long>("Time", out var time)) LevelData.Time = time;
        if (data.TryGetTagValue<byte>("Difficulty", out var difficulty)) LevelData.Difficulty = (Difficulty)difficulty;
        var (chunkX, chunkZ) = LevelData.SpawnPosition.ToChunkCoord();
        var index = 0;
        for (var cx = chunkX - Configuration.SpawnChunkRadius; cx < chunkX + Configuration.SpawnChunkRadius; cx++)
        for (var cz = chunkZ - Configuration.SpawnChunkRadius; cz < chunkZ + Configuration.SpawnChunkRadius; cz++)
        {
            spawnChunks[index++] = NumericsHelper.IntsToLong(cx, cz);
            await GetChunkAsync(cx, cz);
        }
        Loaded = true;
        return true;
    }

    public override async Task SaveAsync()
    {
        await FlushRegionsAsync();
        await using var stream = File.Create(LevelDataFilePath);
        await using var writer = new NbtWriterStream(stream, NbtCompression.GZip, "");
        var spawn = (Vector)LevelData.SpawnPosition.Floor();
        writer.WriteInt("SpawnX", spawn.X);
        writer.WriteInt("SpawnY", spawn.Y);
        writer.WriteInt("SpawnZ", spawn.Z);
        writer.WriteLong("Time", LevelData.Time);
        writer.WriteByte("Difficulty", (byte)LevelData.Difficulty);
        writer.EndCompound();
        await writer.TryFinishAsync();
    }
}
