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

    private bool UsesVanillaLayout => this.ParentWorld is World { UsesVanillaLayout: true };

    protected override string RegionFolderName => this.UsesVanillaLayout ? "region" : "regions";

    public override void Initialize(DimensionCodec codec)
    {
        this.FolderPath = this.UsesVanillaLayout
            ? Path.Combine(this.ParentWorld.FolderPath, VanillaFolder(this.Name))
            : Path.Combine(ServerConstants.WorldsPath, ParentWorld.Name, "dimensions", this.Name.TrimResourceTag(true));

        this.SetDimension(codec);

        this.LevelData = new LevelData
        {
            Time = codec.Element.FixedTime ?? 0,
            DefaultGamemode = GameMode.Survival,
            GeneratorName = Generator.Id,
            GameRules = ParentWorld.LevelData.GameRules,
            // Vanilla's levels share the world's difficulty, which World.SetDifficulty keeps in step.
            Difficulty = ParentWorld.LevelData.Difficulty,
            DifficultyLocked = ParentWorld.LevelData.DifficultyLocked,
            Hardcore = ParentWorld.LevelData.Hardcore
        };

        this.LevelDataFilePath = Path.Combine(this.FolderPath, "level.dat");

        Directory.CreateDirectory(this.FolderPath);
    }

    /// <summary>
    /// A dimension's folder in a vanilla save (vanilla's <c>DimensionType.getStorageFolder</c>): <c>DIM-1</c> for the
    /// nether, <c>DIM1</c> for the end, and <c>dimensions/&lt;namespace&gt;/&lt;path&gt;</c> for others.
    /// </summary>
    internal static string VanillaFolder(string dimension)
    {
        switch (dimension)
        {
            case "minecraft:the_nether":
                return "DIM-1";
            case "minecraft:the_end":
                return "DIM1";
        }

        var id = dimension.Contains(':') ? dimension : $"minecraft:{dimension}";
        var parts = id.Split(':', 2);

        return Path.Combine("dimensions", parts[0], parts[1]);
    }

    public override async Task<bool> LoadAsync(DimensionCodec codec)
    {
        if (!File.Exists(LevelDataFilePath)) return false;
        await using var stream = File.OpenRead(LevelDataFilePath);
        var reader = new NbtReader(stream, NbtCompression.GZip);
        if (reader.ReadNextTag() is not NbtCompound data) return false;
        ReadGameRules(data);
        ReadEndFightNbt(data);
        ReadRaidsNbt(data);
        if (data.TryGetTagValue<int>("SpawnX", out var x) && data.TryGetTagValue<int>("SpawnY", out var y) && data.TryGetTagValue<int>("SpawnZ", out var z))
            LevelData.SpawnPosition = new VectorF(x + 0.5f, y, z + 0.5f);
        if (data.TryGetTagValue<long>("Time", out var time)) LevelData.Time = time;
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
        WriteGameRules(writer);
        WriteEndFightNbt(writer);
        WriteRaidsNbt(writer);
        writer.EndCompound();
        await writer.TryFinishAsync();
    }
}
