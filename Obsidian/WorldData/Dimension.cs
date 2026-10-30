using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.API.Registry.Codecs.Dimensions;
using System.Collections.Frozen;
using System.IO;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.WorldData;

internal sealed class Dimension(ILogger<Dimension> logger, IPacketBroadcaster packetBroadcaster, IOptionsMonitor<ServerConfiguration> configuration,
    IEventDispatcher eventDispatcher, ILevelGenerator worldGenerator, string name, IWorld world) :
    AbstractLevel(logger, packetBroadcaster, configuration, eventDispatcher, worldGenerator, name, world.Seed), IDimension
{
    // Vanilla's dimensions, whose folders no other dimension may take even before they're loaded.
    private static readonly string[] vanillaDimensions = ["minecraft:overworld", "minecraft:the_nether", "minecraft:the_end"];

    public IWorld ParentWorld { get; } = world;

    public override void Initialize(DimensionCodec codec)
    {
        var worldFolder = this.ParentWorld is World world
            ? world.WorldFolderPath
            : Path.Combine(ServerConstants.WorldsPath, ParentWorld.Name);

        var folder = WorldLayout.LevelFolder(this.Name);
        this.FolderPath = Path.Combine(worldFolder, folder);
        CheckInWorld(worldFolder, this.FolderPath);
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

        // A dimension has no level.dat of its own: vanilla keeps what it has of it in the world's, and the rest is in
        // Obsidian's state.
        this.LevelDataFilePath = this.StatePath;

        Directory.CreateDirectory(this.FolderPath);
    }

    // Plugins name their dimensions, and a folder is made from the name, so one that would leave the world's
    // dimensions folder is refused.
    private void CheckInWorld(string worldFolder, string path)
    {
        var root = Path.Combine(worldFolder, "dimensions");
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

        if (!Path.GetFullPath(path).StartsWith(rootPath, StringComparison.Ordinal))
            throw new InvalidOperationException($"{this.Name}'s folder {path} would be outside {root}.");
    }

    // A dimension needs a folder of its own: two identifiers can map to one folder on Windows, like ruby. and ruby, or
    // ones differing only by case. So every part of the folder must be portable, and it can't be another dimension's
    // folder (the world's own level's, one of its dimensions', or vanilla's, which may not be loaded yet), or one of the
    // folders a dimension keeps its data in. Like vanilla, a dimension may sit in another's folder otherwise:
    // ruby:foo/bar's chunks don't mix with ruby:foo's.
    private void CheckOwnFolder(string worldFolder, string folder)
    {
        if (!folder.Split(Path.DirectorySeparatorChar).All(PluginTags.IsPortableName))
            throw new InvalidOperationException($"{this.Name}'s folder {folder} isn't a portable name.");

        var others = vanillaDimensions
            .Where(name => name != this.Name)
            .Select(name => Path.Combine(worldFolder, WorldLayout.LevelFolder(name)))
            .Concat(this.ParentWorld is World world
                ? world.dimensions.Values
                    .OfType<Dimension>()
                    .Where(dimension => dimension != this)
                    .Select(dimension => dimension.FolderPath)
                    .Append(world.FolderPath)
                : []);

        var path = WithSeparator(this.FolderPath);

        foreach (var other in others.Select(WithSeparator))
        {
            if (Overlaps(path, other) || Overlaps(other, path))
                throw new InvalidOperationException($"{this.Name}'s folder {this.FolderPath} would overlap {other}.");
        }

        static string WithSeparator(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;

        // Whether the inner folder is the outer one, or one of the outer dimension's data folders or in one. Windows'
        // folders ignore case, so folders differing only by case count as one.
        static bool Overlaps(string inner, string outer)
        {
            if (!inner.StartsWith(outer, StringComparison.OrdinalIgnoreCase))
                return false;

            var below = inner[outer.Length..].Split(Path.DirectorySeparatorChar, 2)[0];
            return below.Length == 0 || dataFolders.Contains(below);
        }
    }

    // The folders a dimension keeps its data in, which no other dimension's folder may be.
    private static readonly FrozenSet<string> dataFolders = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "region",
        "entities",
        "poi",
        "data");

    /// <summary>
    /// Whether the dimension was saved before: it has Obsidian's state or chunks in its folder. Known once it's
    /// initialized.
    /// </summary>
    /// <remarks>
    /// Region files alone don't count: generating a new dimension creates them before it saves any chunk, so a
    /// cancelled creation can leave them empty.
    /// </remarks>
    internal bool IsSaved
    {
        get
        {
            var regions = Path.Combine(this.FolderPath, this.RegionFolderName);

            return File.Exists(this.StatePath)
                || (Directory.Exists(regions) && Directory.EnumerateFiles(regions, "*.mca").Any(RegionFile.HasChunks));
        }
    }

    public override async Task<bool> LoadAsync(DimensionCodec codec)
    {
        if (!this.IsSaved)
            return false;

        this.ReadLevelState();

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

    // Vanilla keeps no spawn or time of a dimension's own, so they're in Obsidian's state.
    protected override void ReadOwnState(NbtCompound state)
    {
        if (state.TryGetTagValue<int>("SpawnX", out var x) && state.TryGetTagValue<int>("SpawnY", out var y) && state.TryGetTagValue<int>("SpawnZ", out var z))
            LevelData.SpawnPosition = new VectorF(x + 0.5f, y, z + 0.5f);
        if (state.TryGetTagValue<long>("Time", out var time))
            LevelData.Time = time;
    }

    protected override void WriteOwnState(INbtWriter writer)
    {
        var spawn = (Vector)LevelData.SpawnPosition.Floor();
        writer.WriteInt("SpawnX", spawn.X);
        writer.WriteInt("SpawnY", spawn.Y);
        writer.WriteInt("SpawnZ", spawn.Z);
        writer.WriteLong("Time", LevelData.Time);
    }

    public override async Task SaveAsync()
    {
        await FlushRegionsAsync();

        // Under the world's save lock, as the world's saves write the dimension's state too.
        if (this.ParentWorld is World world)
            await world.SaveExclusiveAsync(this.SaveLevelState);
        else
            this.SaveLevelState();
    }
}
