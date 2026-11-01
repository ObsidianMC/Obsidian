using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API.Configuration;
using Obsidian.Entities;
using Obsidian.Nbt;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace Obsidian.WorldData.Upgrade;

/// <summary>
/// Upgrades, once, a world Obsidian saved in 1.21.11's layout to vanilla 26.3's (see <see cref="WorldLayout"/>), after
/// copying the whole world to a backup folder beside it (<c>&lt;world&gt;-1.21.11-backup</c>).
/// </summary>
/// <remarks>
/// <para>
/// Obsidian saved worlds in two layouts: its own (<c>regions/</c>, a level.dat without <c>Data</c>, and its dimensions
/// in <c>dimensions/&lt;path&gt;</c> for vanilla's and <c>dimensions/&lt;namespace&gt;/&lt;path&gt;</c> for plugins',
/// each with a level.dat of its own), and vanilla 1.21.11's for an integrated server's save (<c>region/</c>,
/// <c>DIM-1/</c>, <c>DIM1/</c>, <c>playerdata/</c>, and everything in level.dat's <c>Data</c>). The upgrade moves their
/// files where 26.3 keeps them, splits the level data into 26.3's saved data files, and passes every chunk, entity chunk
/// and structure template through <see cref="LegacyChunkUpgrade"/>.
/// </para>
/// <para>
/// A world is upgraded when it shows any part of either layout, not by its <c>DataVersion</c>: builds of Obsidian for
/// 26.3 wrote 26.3's data version in 1.21.11's layout. This isn't a general vanilla data fixer: what vanilla 1.21.11 kept
/// that Obsidian doesn't model is moved where 26.3 looks for it, but its contents are left for vanilla to fix.
/// </para>
/// <para>
/// Until the upgrade finishes, a marker file in the world names its backup, so an upgrade that stopped partway is
/// redone from the backup the next time the world loads.
/// </para>
/// <para>
/// Before anything is backed up or moved, every file the conversion reads (region and entity chunks, structure
/// templates and Obsidian's level.dat of a dimension) is read and upgraded in memory the same way. A world with chunks
/// that an Obsidian build before its vanilla-format saves (#539) wrote (see
/// <see cref="LegacyChunkUpgrade.HasPaletteIds"/>), or with a file the conversion couldn't read, is refused with a
/// <see cref="WorldLoadException"/>. It's left as it was, apart from <c>session.lock</c>, which loading takes first.
/// </para>
/// </remarks>
internal static partial class WorldUpgrade
{
    private const string MarkerFile = "obsidian_upgrade_backup.txt";

    // In a backup being copied: the world it's a copy of, so only a copy of that world is taken for one that stopped.
    private const string StagingFile = "obsidian_upgrade_staging.txt";

    private const string SessionLockFile = "session.lock";

    private const string Overworld = "minecraft:overworld";
    private const string TheNether = "minecraft:the_nether";
    private const string TheEnd = "minecraft:the_end";

    // The folders a level keeps its data in, in either layout.
    private static readonly string[] levelFolders = ["region", "regions", "entities", "poi", "data"];

    // Folders only 1.21.11's layouts have in a world's folder.
    private static readonly string[] legacyFolders =
        ["region", "regions", "entities", "poi", "DIM-1", "DIM1", "playerdata", "stats", "advancements"];

    /// <summary>
    /// Whether the world in <paramref name="folder"/> is in one of 1.21.11's layouts, or an upgrade of it stopped partway.
    /// </summary>
    /// <remarks>
    /// An empty legacy folder doesn't count: it holds nothing to upgrade, and one left in a 26.3 world (by a tool, or a
    /// player tidying up) would have the world upgraded for nothing.
    /// </remarks>
    public static bool IsNeeded(string folder)
    {
        if (!Directory.Exists(folder))
            return false;

        if (File.Exists(Path.Combine(folder, MarkerFile))
            || legacyFolders.Any(name => HasFiles(Path.Combine(folder, name)))
            || LegacyLevelFolders(Path.Combine(folder, "dimensions")).Any())
        {
            return true;
        }

        var data = Path.Combine(folder, "data");
        if (Directory.Exists(Path.Combine(data, "obsidian", "world"))
            || (Directory.Exists(data) && Directory.EnumerateFiles(data, "*.dat").Any(file => LegacyWorldDataFile(Path.GetFileName(file)) is not null)))
        {
            return true;
        }

        return World.ReadLevelDataOrBackup(Path.Combine(folder, WorldLayout.LevelDataFile), NullLogger.Instance) is NbtCompound root
            && !IsCurrentLevelData(root);
    }

    // Whether a level.dat is 26.3's. Obsidian's had no Data; 1.21.11's Data kept the game rules and world generation
    // settings.
    private static bool IsCurrentLevelData(NbtCompound root) =>
        root.TryGetTag<NbtCompound>("Data", out var data)
        && data.HasTag("version_history")
        && !data.HasTag("game_rules")
        && !data.HasTag("WorldGenSettings");

    private static bool HasFiles(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();

    /// <summary>
    /// Upgrades the world in <paramref name="folder"/>, which the caller holds the session lock of, if it needs it (see
    /// <see cref="IsNeeded"/>).
    /// </summary>
    /// <param name="rootDimension">The dimension of the world's own level, whose chunks are in the world's folder.</param>
    /// <exception cref="IOException">The upgrade can't go on; the world is left for the next load to try again.</exception>
    /// <exception cref="WorldLoadException">The world can't be upgraded: an Obsidian build from before its vanilla-format
    /// saves wrote its chunks, or a file the conversion reads can't be read. Nothing was written.</exception>
    public static async Task UpgradeAsync(string folder, string rootDimension, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!IsNeeded(folder))
            return;

        var marker = Path.Combine(folder, MarkerFile);
        var backup = File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
        if (backup is not null && !Directory.Exists(backup))
        {
            throw new IOException($"An upgrade of the world in {folder} stopped partway, and its backup {backup} is "
                + "missing. Restore the world from a backup of your own, then remove " + MarkerFile + ".");
        }

        // Checked before anything is written. A redone upgrade restores from the backup, so that's what's checked.
        await CheckAsync(backup ?? folder, folder, cancellationToken);

        if (backup is not null)
        {
            Log.Redoing(logger, folder, backup);
            Restore(backup, folder);
        }
        else
        {
            backup = Backup(folder);
            File.WriteAllText(marker, backup);
        }

        Log.Upgrading(logger, folder, backup);

        await ConvertAsync(folder, rootDimension, logger, cancellationToken);

        // What's left would have the world upgraded again on every load, from a backup of a world already upgraded.
        File.Delete(marker);
        if (IsNeeded(folder))
        {
            File.WriteAllText(marker, backup);
            throw new IOException($"The upgrade of the world in {folder} left files in 1.21.11's layout. Move them out of "
                + $"the world, or restore it from {backup} and remove {MarkerFile}.");
        }
    }

    // The folders a level keeps its chunks in, in either layout.
    private static readonly string[] regionFolders = ["region", "regions"];

    // Establishes that the world in source (the world, or the backup a stopped upgrade is redone from) can be upgraded,
    // before anything is written: every file the conversion reads is read and upgraded in memory, with the same code.
    private static async Task CheckAsync(string source, string folder, CancellationToken cancellationToken)
    {
        // The world's own level, vanilla's nether and end, and Obsidian's and 26.3's levels in dimensions/.
        var levels = new[] { source, Path.Combine(source, "DIM-1"), Path.Combine(source, "DIM1") }
            .Concat(LevelFolders(Path.Combine(source, "dimensions")));
        var scratch = Directory.CreateTempSubdirectory("obsidian-upgrade-check-");

        try
        {
            foreach (var level in levels)
            {
                foreach (var name in regionFolders)
                {
                    await CheckRegionsAsync(Path.Combine(level, name), LegacyChunkUpgrade.UpgradeChunk, source, folder,
                        scratch.FullName, cancellationToken);
                }

                await CheckRegionsAsync(Path.Combine(level, "entities"), LegacyChunkUpgrade.UpgradeEntityChunk, source,
                    folder, scratch.FullName, cancellationToken);
            }
        }
        finally
        {
            scratch.Delete(recursive: true);
        }

        foreach (var level in LegacyLevelFolders(Path.Combine(source, "dimensions")))
            Check(Path.Combine(level, WorldLayout.LevelDataFile), path => ReadRoot(path), source, folder);

        var generated = Path.Combine(source, "generated");
        if (Directory.Exists(generated))
        {
            foreach (var template in Directory.EnumerateFiles(generated, "*.nbt", SearchOption.AllDirectories))
            {
                Check(template, path => ReadTemplate(path) is NbtCompound read
                    ? LegacyChunkUpgrade.UpgradeStructureTemplate(read)
                    : null, source, folder);
            }
        }
    }

    // Reads and upgrades a level's region files like UpgradeRegionsAsync, from a copy of their folder (opening a region
    // file can repair it and reclaim its temporary files, which mustn't happen to the world before it's backed up).
    private static async Task CheckRegionsAsync(string regions, Func<NbtCompound, NbtCompound> upgrade, string source,
        string folder, string scratch, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(regions))
            return;

        var copy = Path.Combine(scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);

        try
        {
            foreach (var file in Directory.EnumerateFiles(regions))
                File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));

            foreach (var path in Directory.GetFiles(copy, "r.*.mca"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var original = Path.Combine(regions, Path.GetFileName(path));
                try
                {
                    await CheckRegionAsync(path, upgrade);
                }
                catch (WorldLoadException)
                {
                    throw Refused(folder, source, original);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    throw Unreadable(folder, source, original, ex);
                }
            }
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    private static async Task CheckRegionAsync(string path, Func<NbtCompound, NbtCompound> upgrade)
    {
        await using var file = new RegionFile(path, NbtCompression.ZLib);
        await file.InitializeAsync();

        for (var x = 0; x < 32; x++)
        {
            for (var z = 0; z < 32; z++)
            {
                if (!file.HasChunk(x, z) || await file.GetChunkBytesAsync(x, z) is not Memory<byte> bytes)
                    continue;

                if (NbtReader.ReadTag(bytes.Span, true, out _) is not NbtCompound chunk)
                    continue;

                if (LegacyChunkUpgrade.HasPaletteIds(chunk))
                    throw new WorldLoadException("The chunk's palettes name block states by 1.21.11 state id.");

                upgrade(chunk);
            }
        }
    }

    // Reads and upgrades one file in memory, refusing the world when it can't be.
    private static void Check(string path, Func<string, NbtCompound?> read, string source, string folder)
    {
        if (!File.Exists(path))
            return;

        try
        {
            read(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw Unreadable(folder, source, path, ex);
        }
    }

    private static WorldLoadException Refused(string folder, string source, string path) =>
        new($"The world in {folder} was saved by an Obsidian build from before Obsidian saved chunks in vanilla's "
            + "format, and can't be upgraded to Minecraft 26.3: its chunks (such as in "
            + $"{Path.GetRelativePath(source, path)}) name block states by 1.21.11 state ids, without the properties "
            + "those states need. Such worlds aren't supported. The world was left unchanged.");

    private static WorldLoadException Unreadable(string folder, string source, string path, Exception exception) =>
        new($"The world in {folder} can't be upgraded to Minecraft 26.3: {Path.GetRelativePath(source, path)} can't be "
            + $"read ({exception.Message}). Repair or remove that file, or restore the world from a backup of your own. "
            + "The world was left unchanged.", exception);

    /// <summary>
    /// The folders of the levels in <paramref name="folder"/> and below: ones with a level.dat or a folder a level keeps
    /// its data in. A level's data folders aren't searched, as only levels nest in a level, so what a plugin keeps in
    /// them (an archived <c>region</c> folder, say) is left alone.
    /// </summary>
    private static IEnumerable<string> LevelFolders(string folder)
    {
        if (!Directory.Exists(folder))
            yield break;

        var isLevel = File.Exists(Path.Combine(folder, WorldLayout.LevelDataFile))
            || levelFolders.Any(name => Directory.Exists(Path.Combine(folder, name)));

        if (isLevel)
            yield return folder;

        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            if (isLevel && levelFolders.Contains(Path.GetFileName(child)))
                continue;

            foreach (var nested in LevelFolders(child))
                yield return nested;
        }
    }

    private static async Task ConvertAsync(string folder, string rootDimension, ILogger logger,
        CancellationToken cancellationToken)
    {
        var dimensions = Path.Combine(folder, "dimensions");
        var rootLevel = Path.Combine(folder, WorldLayout.LevelFolder(rootDimension));

        // A world whose level.dat is 26.3's has only stray legacy folders: its level data and world data are already
        // where 26.3 keeps them, and must stay as they are.
        var levelDataIsCurrent = World.ReadLevelDataOrBackup(Path.Combine(folder, WorldLayout.LevelDataFile), logger)
            is NbtCompound levelRoot && IsCurrentLevelData(levelRoot);

        // Obsidian's dimensions, found before the world's own level moves into dimensions/. In Obsidian's layout,
        // vanilla's dimensions had no namespace folder.
        foreach (var legacy in LegacyLevelFolders(dimensions).ToArray())
        {
            var parts = Path.GetRelativePath(dimensions, legacy).Split(Path.DirectorySeparatorChar);
            var dimension = parts.Length == 1 ? $"minecraft:{parts[0]}" : $"{parts[0]}:{string.Join('/', parts[1..])}";

            UpgradeLevel(legacy, Path.Combine(folder, WorldLayout.LevelFolder(dimension)));
        }

        // Vanilla's nether and end, whose folders hold nothing else.
        foreach (var (legacy, dimension) in new[] { ("DIM-1", TheNether), ("DIM1", TheEnd) })
        {
            var level = Path.Combine(folder, WorldLayout.LevelFolder(dimension));
            MoveFolder(Path.Combine(folder, legacy), level);
            UpgradeLevel(level, level);
        }

        // The world's own level, whose data folder is shared with the world's.
        foreach (var name in levelFolders.Where(name => name != "data"))
            MoveFolder(Path.Combine(folder, name), Path.Combine(rootLevel, name));
        UpgradeLevel(rootLevel, rootLevel);
        UpgradeWorldData(folder, rootLevel, levelDataIsCurrent);

        MoveFolder(Path.Combine(folder, "playerdata"), Path.Combine(folder, WorldLayout.PlayerDataFolder));
        MoveFolder(Path.Combine(folder, "stats"), Path.Combine(folder, "players", "stats"));
        MoveFolder(Path.Combine(folder, "advancements"), Path.Combine(folder, "players", "advancements"));

        if (!levelDataIsCurrent)
            await SplitLevelDataAsync(folder, rootLevel, logger);

        // Vanilla 26.3 renamed the folder of structures saved in the world.
        var generated = Path.Combine(folder, "generated");
        if (Directory.Exists(generated))
        {
            foreach (var space in Directory.GetDirectories(generated))
                MoveFolder(Path.Combine(space, "structures"), Path.Combine(space, "structure"));

            foreach (var template in Directory.EnumerateFiles(generated, "*.nbt", SearchOption.AllDirectories))
                UpgradeTemplate(template);
        }

        if (Directory.Exists(dimensions))
        {
            // The levels' own folders, as CheckAsync read them: not what a level keeps in its data folders.
            foreach (var level in LevelFolders(dimensions).ToArray())
            {
                await UpgradeRegionsAsync(Path.Combine(level, WorldLayout.RegionFolder), LegacyChunkUpgrade.UpgradeChunk,
                    cancellationToken);
                await UpgradeRegionsAsync(Path.Combine(level, "entities"), LegacyChunkUpgrade.UpgradeEntityChunk,
                    cancellationToken);
            }

            DeleteEmptyFolders(dimensions);
        }
    }

    /// <summary>
    /// The folders of Obsidian's levels in <paramref name="folder"/> and below: ones with a level.dat or a non-empty
    /// <c>regions</c> folder of their own. A level's data folders aren't searched, as only levels nest in a level.
    /// </summary>
    private static IEnumerable<string> LegacyLevelFolders(string folder)
    {
        if (!Directory.Exists(folder))
            yield break;

        var isLevel = File.Exists(Path.Combine(folder, WorldLayout.LevelDataFile))
            || levelFolders.Any(name => name != "data" && Directory.Exists(Path.Combine(folder, name)));

        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            if (isLevel && levelFolders.Contains(Path.GetFileName(child)))
                continue;

            if (File.Exists(Path.Combine(child, WorldLayout.LevelDataFile)) || HasFiles(Path.Combine(child, "regions")))
                yield return child;

            foreach (var nested in LegacyLevelFolders(child))
                yield return nested;
        }
    }

    /// <summary>
    /// Moves a level's folders from <paramref name="from"/> to <paramref name="to"/> (the same folder for one already in
    /// place) and upgrades them: <c>regions</c> becomes <c>region</c>, Obsidian's level.dat becomes the level's state
    /// (see <see cref="AbstractLevel.StateDataId"/>), and vanilla's saved data files go in <c>data/minecraft/</c>.
    /// </summary>
    private static void UpgradeLevel(string from, string to)
    {
        if (!Directory.Exists(from))
            return;

        // Only the level's own folders: other levels may nest in its folder.
        if (!SamePath(from, to))
        {
            foreach (var name in levelFolders)
                MoveFolder(Path.Combine(from, name), Path.Combine(to, name));
        }

        MoveFolder(Path.Combine(to, "regions"), Path.Combine(to, WorldLayout.RegionFolder));

        // Obsidian's level.dat of a dimension: its spawn, time and Obsidian's state. The game rules are the world's.
        var levelData = Path.Combine(from, WorldLayout.LevelDataFile);
        if (ReadRoot(levelData) is NbtCompound saved)
        {
            var state = new NbtCompound("data");
            foreach (var (name, tag) in saved.Where(entry => entry.Key is not ("game_rules" or "GameRules")))
                state.Add(name, tag);

            SavedDataFile.Write(WorldLayout.SavedDataPath(to, AbstractLevel.StateDataId), state);
        }

        File.Delete(levelData);
        File.Delete($"{levelData}.old");

        // Vanilla 1.21.11 kept a level's own saved data loose in its data folder.
        var data = Path.Combine(to, "data");
        if (!Directory.Exists(data))
            return;

        foreach (var file in Directory.GetFiles(data, "*.dat"))
            MoveFile(file, Path.Combine(data, "minecraft", LegacyLevelDataFile(Path.GetFileName(file))));
    }

    // The name 26.3 gives a level's saved data file.
    private static string LegacyLevelDataFile(string name) => name switch
    {
        "chunks.dat" => "chunk_tickets.dat",
        "raids_end.dat" => "raids.dat",
        _ => name
    };

    /// <summary>
    /// Where 26.3 keeps a file 1.21.11 kept loose in the world's data folder, relative to that folder, or <c>null</c> for
    /// one it doesn't know.
    /// </summary>
    private static string? LegacyWorldDataFile(string name)
    {
        if (MapFile().Match(name) is { Success: true } map)
            return Path.Combine("minecraft", "maps", $"{map.Groups[1].Value}.dat");

        if (CommandStorageFile().Match(name) is { Success: true } storage)
            return Path.Combine(storage.Groups[1].Value, "command_storage.dat");

        return name switch
        {
            "idcounts.dat" => Path.Combine("minecraft", "maps", "last_id.dat"),
            "scoreboard.dat" or "random_sequences.dat" or "stopwatches.dat" => Path.Combine("minecraft", name),
            _ => null
        };
    }

    [GeneratedRegex(@"^map_(\d+)\.dat$")]
    private static partial Regex MapFile();

    [GeneratedRegex(@"^command_storage_([a-z0-9_.-]+)\.dat$")]
    private static partial Regex CommandStorageFile();

    /// <summary>
    /// Sorts the world's data folder, which 1.21.11 shared with the world's own level: the world's saved data stays (maps
    /// and vanilla's files moved where 26.3 keeps them, plugins' world data out of <c>data/obsidian/world/</c>), and the
    /// level's goes to <paramref name="rootLevel"/>.
    /// </summary>
    /// <param name="levelDataIsCurrent">
    /// Whether the world's level.dat is 26.3's, so its namespace folders already hold world data.
    /// </param>
    private static void UpgradeWorldData(string folder, string rootLevel, bool levelDataIsCurrent)
    {
        var data = Path.Combine(folder, "data");
        if (!Directory.Exists(data))
            return;

        // Plugins' level data first, as the world's data takes the same folders: command storage becomes
        // data/<namespace>/command_storage.dat, and plugins' world data moves out of data/obsidian/world/.
        if (!levelDataIsCurrent)
        {
            foreach (var space in Directory.GetDirectories(data))
            {
                if (Path.GetFileName(space) is not ("minecraft" or "obsidian"))
                    MoveFolder(space, Path.Combine(rootLevel, "data", Path.GetFileName(space)));
            }
        }

        foreach (var file in Directory.GetFiles(data, "*.dat"))
        {
            var name = Path.GetFileName(file);
            if (LegacyWorldDataFile(name) is string moved)
                MoveFile(file, Path.Combine(data, moved));
            else if (name is "raids.dat" or "raids_end.dat" or "chunks.dat" or "world_border.dat")
                MoveFile(file, Path.Combine(rootLevel, "data", "minecraft", LegacyLevelDataFile(name)));
        }

        var worldData = Path.Combine(data, "obsidian", "world");
        if (Directory.Exists(worldData))
        {
            foreach (var space in Directory.GetDirectories(worldData))
                MoveFolder(space, Path.Combine(data, Path.GetFileName(space)));

            DeleteEmptyFolders(Path.Combine(data, "obsidian"));
        }
    }

    /// <summary>
    /// Splits the world's level.dat into 26.3's files, from either of 1.21.11's shapes: Obsidian's, without <c>Data</c>,
    /// or vanilla's. Obsidian's state beside it goes to the world's own level's state.
    /// </summary>
    private static async Task SplitLevelDataAsync(string folder, string rootLevel, ILogger logger)
    {
        var levelData = Path.Combine(folder, WorldLayout.LevelDataFile);
        if (World.ReadLevelDataOrBackup(levelData, logger) is not NbtCompound root)
            return;

        var files = root.TryGetTag<NbtCompound>("Data", out var data)
            ? await SplitVanillaAsync(folder, data)
            : SplitObsidian(root);

        var state = new NbtCompound("data");
        foreach (var (name, tag) in root.Where(entry => entry.Key.StartsWith("Obsidian", StringComparison.Ordinal)))
            state.Add(name, tag);
        if (state.Count > 0)
            SavedDataFile.Write(WorldLayout.SavedDataPath(rootLevel, AbstractLevel.StateDataId), state);

        // The backups are in 1.21.11's shape, which would be read as 26.3's.
        File.Delete(levelData);
        File.Delete($"{levelData}_old");
        File.Delete($"{levelData}.old");

        VanillaLevelData.Update(files, VanillaLevelData.Read(files));
        files.Save(folder);
    }

    // Obsidian's flat level.dat.
    private static LevelFiles SplitObsidian(NbtCompound root)
    {
        var level = new LevelData
        {
            Hardcore = root.GetBool("hardcore"),
            MapFeatures = root.GetBool("MapFeatures"),
            Raining = root.GetBool("raining"),
            Thundering = root.GetBool("thundering"),
            DefaultGamemode = (GameMode)Get(root, "GameType", 0),
            Difficulty = (Difficulty)Get<byte>(root, "Difficulty", (byte)Difficulty.Normal),
            DifficultyLocked = root.GetBool("DifficultyLocked"),
            RainTime = Get(root, "rainTime", 0),
            ThunderTime = Get(root, "thunderTime", 0),
            // The spawn is saved as a block; players spawn at its center.
            SpawnPosition = new VectorD(Get(root, "SpawnX", 0) + 0.5, Get(root, "SpawnY", 0), Get(root, "SpawnZ", 0) + 0.5),
            Time = Get(root, "Time", 0L)
        };

        var levelName = Get(root, "LevelName", string.Empty)!;
        var generator = Get(root, "generatorName", "minecraft:mojang_generator")!;
        var settings = new NewWorldConfiguration { GenerateStructures = level.MapFeatures };
        var files = VanillaLevelData.Create(settings, levelName, Get(root, "RandomSeed", 0L), generator);

        // Obsidian's single legacy clock tracked both sun time and elapsed game time.
        level.WorldClocks.GameTime = level.Time;

        var rules = root.TryGetTag<NbtCompound>("game_rules", out var gameRules) || root.TryGetTag("GameRules", out gameRules)
            ? gameRules
            : new NbtCompound();
        foreach (var (name, tag) in rules)
        {
            if (AbstractLevel.GameRuleValue(tag) is string value)
                level.GameRules[name] = value;
        }

        VanillaLevelData.Update(files, level);
        return files;
    }

    // Vanilla 1.21.11's Data, as Obsidian's integrated server kept it: what 26.3 moved out of level.dat goes to its
    // saved data files, renamed as vanilla's file fixes rename it. The files start as the world has them, so only the
    // fields Data still holds replace theirs.
    private static async Task<LevelFiles> SplitVanillaAsync(string folder, NbtCompound data)
    {
        var files = LevelFiles.Load(folder, data);

        if (Take(data, "game_rules") is NbtCompound rules)
            MoveEntries(rules, files.GameRules);

        if (Take(data, "WorldGenSettings") is NbtCompound worldGen)
        {
            MoveEntries(worldGen, files.WorldGenSettings);
            Rename(files.WorldGenSettings, "generate_features", "generate_structures");
        }

        foreach (var (from, to) in new[] { ("raining", "raining"), ("thundering", "thundering"), ("rainTime", "rain_time"),
            ("thunderTime", "thunder_time"), ("clearWeatherTime", "clear_weather_time") })
        {
            if (Take(data, from) is INbtTag tag)
                files.Weather.Set(Named(tag, to));
        }

        if (Take(data, "DayTime") is NbtTag<long> dayTime)
            files.WorldClocks.Set(new NbtCompound(Overworld) { new NbtTag<long>("total_ticks", dayTime.Value) });

        if (data.HasTag("Difficulty") || data.HasTag("hardcore") || data.HasTag("DifficultyLocked"))
        {
            var difficulty = new NbtCompound("difficulty_settings")
            {
                new NbtTag<string>("difficulty", ((Difficulty)Get<byte>(data, "Difficulty", (byte)Difficulty.Normal)).ToString().ToLowerInvariant()),
                new NbtTag<byte>("hardcore", (byte)(data.GetBool("hardcore") ? 1 : 0)),
                new NbtTag<byte>("locked", (byte)(data.GetBool("DifficultyLocked") ? 1 : 0))
            };
            Take(data, "Difficulty");
            Take(data, "hardcore");
            Take(data, "DifficultyLocked");
            data.Set(difficulty);
        }

        if (Take(data, "WanderingTraderSpawnDelay") is INbtTag delay)
            files.WanderingTrader.Set(Named(delay, "spawn_delay"));
        if (Take(data, "WanderingTraderSpawnChance") is INbtTag chance)
            files.WanderingTrader.Set(Named(chance, "spawn_chance"));
        Take(data, "WanderingTraderId");

        if (Take(data, "DragonFight") is NbtCompound fight)
        {
            foreach (var (from, to) in new[] { ("NeedsStateScanning", "needs_state_scanning"), ("DragonKilled", "dragon_killed"),
                ("PreviouslyKilled", "previously_killed"), ("Dragon", "dragon_uuid"), ("ExitPortalLocation", "exit_portal_location"),
                ("Gateways", "gateways") })
            {
                Rename(fight, from, to);
            }

            var end = Path.Combine(folder, WorldLayout.LevelFolder(TheEnd));
            SavedDataFile.Write(WorldLayout.SavedDataPath(end, "minecraft:ender_dragon_fight"), fight);
        }

        if (Take(data, "CustomBossEvents") is NbtCompound bossEvents)
            SavedDataFile.Write(WorldLayout.SavedDataPath(folder, "minecraft:custom_boss_events"), bossEvents);

        if (Take(data, "ScheduledEvents") is NbtList events)
        {
            foreach (var scheduled in events.OfType<NbtCompound>())
            {
                Rename(scheduled, "Name", "id");
                Rename(scheduled, "TriggerTime", "trigger_time");
                Rename(scheduled, "Callback", "callback");
                if (scheduled.TryGetTag<NbtCompound>("callback", out var callback))
                {
                    Rename(callback, "Type", "type");
                    Rename(callback, "Name", "id");
                }
            }

            SavedDataFile.Write(WorldLayout.SavedDataPath(folder, "minecraft:scheduled_events"),
                new NbtCompound("data") { Named(events, "events") });
        }

        SplitWorldBorder(folder, data);

        // The singleplayer owner's data, which 26.3 keeps with the other players'.
        if (Take(data, "Player") is NbtCompound player
            && player.TryGetTag<NbtArray<int>>("UUID", out var uuid) && uuid.Count == 4)
        {
            var owner = EntityNbt.UuidFromInts(uuid.GetArray());
            var path = Path.Combine(folder, WorldLayout.PlayerDataFolder, $"{owner}.dat");
            await PlayerDataFile.WriteAsync(path, player);
            VanillaLevelData.SetSingleplayerOwner(files, owner);
        }

        return files;
    }

    // Vanilla 1.21.11's one world border, which 26.3 keeps per dimension: the nether's center in its own coordinates.
    private static void SplitWorldBorder(string folder, NbtCompound data)
    {
        var border = Take(data, "world_border") as NbtCompound ?? new NbtCompound();
        foreach (var (from, to) in new[] { ("BorderCenterX", "center_x"), ("BorderCenterZ", "center_z"), ("BorderSize", "size"),
            ("BorderSizeLerpTime", "lerp_time"), ("BorderSizeLerpTarget", "lerp_target"), ("BorderSafeZone", "safe_zone"),
            ("BorderDamagePerBlock", "damage_per_block"), ("BorderWarningBlocks", "warning_blocks"),
            ("BorderWarningTime", "warning_time") })
        {
            if (Take(data, from) is INbtTag tag)
                border.Set(Named(tag, to));
        }

        if (border.Count == 0)
            return;

        foreach (var (dimension, scale) in new[] { (Overworld, 1.0), (TheNether, 8.0), (TheEnd, 1.0) })
        {
            var copy = new NbtCompound("data");
            foreach (var (name, tag) in border)
            {
                copy.Add(name, name is "center_x" or "center_z" && tag is NbtTag<double> center
                    ? new NbtTag<double>(name, center.Value / scale)
                    : Copy(tag));
            }

            var level = Path.Combine(folder, WorldLayout.LevelFolder(dimension));
            SavedDataFile.Write(WorldLayout.SavedDataPath(level, "minecraft:world_border"), copy);
        }
    }

    private static async Task UpgradeRegionsAsync(string folder, Func<NbtCompound, NbtCompound> upgrade,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var path in Directory.GetFiles(folder, "r.*.mca"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var file = new RegionFile(path, NbtCompression.ZLib);
            await file.InitializeAsync();

            for (var x = 0; x < 32; x++)
            {
                for (var z = 0; z < 32; z++)
                {
                    if (!file.HasChunk(x, z) || await file.GetChunkBytesAsync(x, z) is not Memory<byte> bytes)
                        continue;

                    if (NbtReader.ReadTag(bytes.Span, true, out _) is not NbtCompound chunk)
                        continue;

                    var written = new MemoryStream();
                    await using (var writer = new NbtWriterStream(written, NbtCompression.ZLib, ""))
                    {
                        writer.WriteEntries(upgrade(chunk));
                        writer.EndCompound();
                        await writer.TryFinishAsync();
                    }

                    // Staged, then committed together by the flush below, for two flushes per file.
                    written.TryGetBuffer(out var payload);
                    await file.StageAsync(x, z, payload.AsMemory());
                }
            }

            await file.FlushAsync();
        }
    }

    private static void UpgradeTemplate(string path)
    {
        if (ReadTemplate(path) is not NbtCompound template)
            return;

        var upgraded = LegacyChunkUpgrade.UpgradeStructureTemplate(template);
        var temporary = $"{path}.tmp";
        using (var stream = File.Create(temporary))
        using (var writer = new NbtWriterStream(stream, NbtCompression.GZip, upgraded.Name ?? ""))
        {
            writer.WriteEntries(upgraded);
            writer.EndCompound();
            writer.TryFinish();
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Copies the world to a new folder beside it, through a temporary folder, so a backup that exists is complete.
    /// </summary>
    /// <returns>The backup's folder.</returns>
    private static string Backup(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var backup = $"{full}-1.21.11-backup";
        for (var number = 2; Taken(backup) || (Taken($"{backup}.partial") && !IsStaging($"{backup}.partial")); number++)
            backup = $"{full}-1.21.11-backup-{number}";

        // A temporary folder left by a copy of this world that stopped is incomplete. Anything else of that name was
        // skipped above, as it isn't the upgrade's to delete.
        var temporary = $"{backup}.partial";
        if (Directory.Exists(temporary))
            Directory.Delete(temporary, recursive: true);

        var staging = Path.Combine(temporary, StagingFile);
        Directory.CreateDirectory(temporary);
        File.WriteAllText(staging, full);
        Copy(full, temporary);
        File.Delete(staging);
        Directory.Move(temporary, backup);
        return backup;

        static bool Taken(string path) => Directory.Exists(path) || File.Exists(path);

        bool IsStaging(string path)
        {
            var named = Path.Combine(path, StagingFile);
            return File.Exists(named) && File.ReadAllText(named).Trim() == full;
        }
    }

    // Puts the world back as the backup has it, but its session lock and the marker.
    private static void Restore(string backup, string folder)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder).ToArray())
        {
            if (Path.GetFileName(entry) is SessionLockFile or MarkerFile)
                continue;

            if (Directory.Exists(entry))
                Directory.Delete(entry, recursive: true);
            else
                File.Delete(entry);
        }

        Copy(backup, folder);
    }

    // Copies a world's files, but its session lock, which is locked, and the marker.
    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            if (Path.GetFileName(file) is not (SessionLockFile or MarkerFile))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(from))
            CopyFolder(directory, Path.Combine(to, Path.GetFileName(directory)));

        static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));

            foreach (var directory in Directory.EnumerateDirectories(from))
                CopyFolder(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }

    /// <summary>
    /// Moves a folder, merging it into one that's there already.
    /// </summary>
    /// <exception cref="IOException">A file is in both.</exception>
    private static void MoveFolder(string from, string to)
    {
        if (!Directory.Exists(from) || SamePath(from, to))
            return;

        if (!Directory.Exists(to))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Directory.Move(from, to);
            return;
        }

        foreach (var file in Directory.GetFiles(from))
            MoveFile(file, Path.Combine(to, Path.GetFileName(file)));

        foreach (var directory in Directory.GetDirectories(from))
            MoveFolder(directory, Path.Combine(to, Path.GetFileName(directory)));

        Directory.Delete(from);
    }

    // Moves a file, refusing to replace one: in a world in 1.21.11's layout, nothing is in 26.3's places yet.
    private static void MoveFile(string from, string to)
    {
        if (File.Exists(to))
            throw new IOException($"Both {from} and {to} exist; the world can't be upgraded until one is removed.");

        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to);
    }

    private static void DeleteEmptyFolders(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var child in Directory.GetDirectories(folder))
            DeleteEmptyFolders(child);

        if (!Directory.EnumerateFileSystemEntries(folder).Any())
            Directory.Delete(folder);
    }

    private static bool SamePath(string first, string second) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
        StringComparison.OrdinalIgnoreCase);

    // A gzipped root compound, or null when there's no file.
    private static NbtCompound? ReadTemplate(string path)
    {
        using var stream = File.OpenRead(path);
        return new NbtReader(stream, NbtCompression.GZip).ReadNextTag() as NbtCompound;
    }

    private static NbtCompound? ReadRoot(string path)
    {
        if (!File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        return new NbtReader(stream, NbtCompression.GZip).ReadNextTag() as NbtCompound;
    }

    private static INbtTag? Take(NbtCompound compound, string name)
    {
        if (!compound.TryGetTag(name, out var tag))
            return null;

        compound.Remove(name);
        return tag;
    }

    private static void Rename(NbtCompound compound, string from, string to)
    {
        if (Take(compound, from) is INbtTag tag)
            compound.Set(Named(tag, to));
    }

    private static INbtTag Named(INbtTag tag, string name)
    {
        tag.Name = name;
        return tag;
    }

    private static void MoveEntries(NbtCompound from, NbtCompound to)
    {
        foreach (var (name, tag) in from.ToArray())
            to.Set(Named(tag, name));
    }

    private static INbtTag Copy(INbtTag tag)
    {
        using var writer = new RawNbtWriter(string.Empty);
        writer.WriteTag(tag);
        writer.EndCompound();
        return ((NbtCompound)NbtReader.ReadTag(writer.Data, true, out _)!)[tag.Name!];
    }

    private static T Get<T>(NbtCompound compound, string name, T fallback) =>
        compound.TryGetTagValue<T>(name, out var value) ? value : fallback;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "Upgrading the world in {Folder} to Minecraft 26.3's layout; the world as it was is in {Backup}")]
        public static partial void Upgrading(ILogger logger, string folder, string backup);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "An upgrade of the world in {Folder} stopped partway; redoing it from {Backup}")]
        public static partial void Redoing(ILogger logger, string folder, string backup);
    }
}
