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
    // The dimensions besides the overworld that vanilla's worlds have.
    private static readonly string[] vanillaDimensions = ["minecraft:the_nether", "minecraft:the_end"];

    public IWorld ParentWorld { get; } = world;

    private bool UsesVanillaLayout => this.ParentWorld is World { UsesVanillaLayout: true };

    protected override string RegionFolderName => this.UsesVanillaLayout ? "region" : "regions";

    public override void Initialize(DimensionCodec codec)
    {
        var worldFolder = this.UsesVanillaLayout
            ? this.ParentWorld.FolderPath
            : Path.Combine(ServerConstants.WorldsPath, ParentWorld.Name);

        var folder = this.Folder(this.Name);
        this.FolderPath = Path.Combine(worldFolder, folder);
        CheckInWorld(worldFolder, folder, this.FolderPath);
        this.CheckOwnFolder(worldFolder, folder);

        // A dimension is named after its level stem, which plugins' dimensions don't share with their type.
        this.SetDimension(this.Name, codec);

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

    // Plugins name their dimensions, and a folder is made from the name, so one that would leave the world's
    // dimensions folder is refused (the nether's and end's vanilla folders sit in the world's folder itself).
    private void CheckInWorld(string worldFolder, string folder, string path)
    {
        var root = folder is "DIM-1" or "DIM1" ? worldFolder : Path.Combine(worldFolder, "dimensions");
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

        if (!Path.GetFullPath(path).StartsWith(rootPath, StringComparison.Ordinal))
            throw new InvalidOperationException($"{this.Name}'s folder {path} would be outside {root}.");
    }

    // A dimension needs a folder of its own: two identifiers can map to one folder, like ruby:world and
    // minecraft:ruby/world in Obsidian's layout, or a name Windows reads differently, like ruby. for ruby. So every
    // part of the folder must be portable, and it can't be, hold or sit in another dimension's folder: one this world
    // has, or vanilla's nether's or end's, which may not be loaded yet.
    private void CheckOwnFolder(string worldFolder, string folder)
    {
        if (!folder.Split(Path.DirectorySeparatorChar).All(PluginTags.IsPortableName))
            throw new InvalidOperationException($"{this.Name}'s folder {folder} isn't a portable name.");

        var others = vanillaDimensions
            .Where(name => name != this.Name)
            .Select(name => Path.Combine(worldFolder, this.Folder(name)))
            .Concat(this.ParentWorld is World world
                ? world.dimensions.Values
                    .OfType<Dimension>()
                    .Where(dimension => dimension != this)
                    .Select(dimension => dimension.FolderPath)
                : []);

        var path = WithSeparator(this.FolderPath);

        foreach (var other in others.Select(WithSeparator))
        {
            // Windows' folders ignore case, so folders differing only by case count as one.
            if (path.StartsWith(other, StringComparison.OrdinalIgnoreCase)
                || other.StartsWith(path, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{this.Name}'s folder {this.FolderPath} would overlap {other}.");
            }
        }

        static string WithSeparator(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
    }

    private string Folder(string dimension) =>
        this.UsesVanillaLayout ? VanillaFolder(dimension) : ObsidianFolder(dimension);

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

    /// <summary>
    /// A dimension's folder in Obsidian's layout: <c>dimensions/&lt;path&gt;</c> for vanilla's, and
    /// <c>dimensions/&lt;namespace&gt;/&lt;path&gt;</c> for plugins', as vanilla stores data packs' dimensions.
    /// </summary>
    private static string ObsidianFolder(string dimension)
    {
        var id = dimension.Contains(':') ? dimension : $"minecraft:{dimension}";
        var parts = id.Split(':', 2);

        return parts[0] == "minecraft"
            ? Path.Combine("dimensions", parts[1])
            : Path.Combine("dimensions", parts[0], parts[1]);
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
