using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Upgrade;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Dimension = Obsidian.WorldData.Dimension;

namespace Obsidian.Tests;

/// <summary>
/// Worlds in vanilla 26.3's layout: one vanilla saved opens and saves without losing its data, and worlds Obsidian saved in
/// 1.21.11's layouts are upgraded to it.
/// </summary>
public sealed class WorldFolders : IDisposable
{
    private static readonly Guid playerUuid = Guid.Parse("4566e69f-c907-48ee-8d71-d7ba5aa00d20");

    private readonly string root = Path.Combine(Path.GetTempPath(), $"obsidian-layout-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(this.root))
            Directory.Delete(this.root, recursive: true);
    }

    [Fact(DisplayName = "A world vanilla 26.3 saved loads what Obsidian models, and saving it keeps vanilla's data")]
    public async Task VanillaWorldRoundTrips()
    {
        var folder = Path.Combine(this.root, "world");
        CopyFolder(Path.Combine(AppContext.BaseDirectory, "Assets", "vanilla-26.3-world"), folder);
        var vanilla = ReadDataFiles(folder);

        Assert.False(WorldUpgrade.IsNeeded(folder));

        await using var world = CreateWorld(folder, "777");
        await LoadDimensionsAsync(world);

        var level = world.LevelData;
        Assert.Equal("world", level.LevelName);
        Assert.Equal(Difficulty.Easy, level.Difficulty);
        Assert.Equal(GameMode.Survival, level.DefaultGamemode);
        Assert.Equal(new VectorD(0.5, -60, 0.5), level.SpawnPosition);
        Assert.Equal(1564, level.Time);
        Assert.True(level.Raining);
        Assert.Equal(5436, level.RainTime);
        Assert.True(level.GetBooleanRule("keep_inventory", defaultValue: false));
        Assert.Equal(777, level.RandomSeed);
        Assert.False(level.MapFeatures);

        await world.SaveAsync();
        var saved = ReadDataFiles(folder);

        // What Obsidian always sets when it saves: the time of the save, and that it ran the world.
        string[] changed = ["level.dat/Data/LastPlayed", "level.dat/Data/ServerBrands", "level.dat/Data/WasModded"];
        foreach (var (path, compound) in vanilla)
        {
            Assert.True(saved.TryGetValue(path, out var savedCompound), $"{path} is gone");
            AssertKept(compound, savedCompound, path, changed);
        }
    }

    [Theory(DisplayName = "A vanilla 26.3 world with stray legacy folders keeps its level data and world data")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrayLegacyFoldersKeepVanillaData(bool withFiles)
    {
        var folder = Path.Combine(this.root, "world");
        CopyFolder(Path.Combine(AppContext.BaseDirectory, "Assets", "vanilla-26.3-world"), folder);
        WriteSavedData(Path.Combine(folder, "data", "sample", "global.dat"), new NbtCompound("data"));

        // An empty legacy folder holds nothing to upgrade; one with files is upgraded, around the 26.3 data.
        Directory.CreateDirectory(Path.Combine(folder, "region"));
        if (withFiles)
            WriteText(Path.Combine(folder, "stats", $"{playerUuid}.json"), "{}");
        var vanilla = ReadDataFiles(folder);

        Assert.Equal(withFiles, WorldUpgrade.IsNeeded(folder));
        await WorldUpgrade.UpgradeAsync(folder, "minecraft:overworld", NullLogger.Instance);
        Assert.False(WorldUpgrade.IsNeeded(folder));

        var upgraded = ReadDataFiles(folder);
        foreach (var (path, compound) in vanilla)
        {
            Assert.True(upgraded.TryGetValue(path, out var upgradedCompound), $"{path} is gone");
            AssertKept(compound, upgradedCompound, path, []);
        }
        if (withFiles)
            Assert.True(File.Exists(Path.Combine(folder, $"players/stats/{playerUuid}.json")));

        await using var world = CreateWorld(folder, "1234");
        await LoadDimensionsAsync(world);
        Assert.Equal(777, world.LevelData.RandomSeed);
        Assert.Equal(Difficulty.Easy, world.LevelData.Difficulty);
        Assert.True(world.LevelData.GetBooleanRule("keep_inventory", defaultValue: false));
    }

    [Theory(DisplayName = "A world Obsidian saved in a 1.21.11 layout is upgraded once, after a complete backup")]
    [InlineData("obsidian", 5000)]
    [InlineData("vanilla", 9000)]
    public async Task LegacyWorldsUpgrade(string layout, long gameTime)
    {
        var folder = Path.Combine(this.root, layout);
        if (layout == "obsidian")
            await WriteObsidianWorldAsync(folder);
        else
            await WriteVanillaWorldAsync(folder);

        var original = ReadFiles(folder);
        Assert.True(WorldUpgrade.IsNeeded(folder));

        await WorldUpgrade.UpgradeAsync(folder, "minecraft:overworld", NullLogger.Instance);

        var backup = $"{folder}-1.21.11-backup";
        Assert.Equal(original, ReadFiles(backup));
        Assert.False(WorldUpgrade.IsNeeded(folder));

        foreach (var file in new[] { "dimensions/minecraft/overworld/region/r.0.0.mca", "dimensions/minecraft/the_nether/region/r.0.0.mca",
            $"players/data/{playerUuid}.dat", "data/minecraft/game_rules.dat", "data/minecraft/weather.dat",
            "data/minecraft/world_clocks.dat", "data/minecraft/world_gen_settings.dat" })
        {
            Assert.True(File.Exists(Path.Combine(folder, file)), $"{file} is missing");
        }

        foreach (var legacy in new[] { "region", "regions", "DIM-1", "DIM1", "playerdata", "dimensions/the_nether", "dimensions/the_end" })
            Assert.False(Directory.Exists(Path.Combine(folder, legacy)), $"{legacy} is left");

        var data = (NbtCompound)ReadRoot(Path.Combine(folder, "level.dat"))["Data"];
        Assert.False(data.HasTag("game_rules"));
        Assert.False(data.HasTag("WorldGenSettings"));
        Assert.True(data.HasTag("version_history"));

        await using var world = CreateWorld(folder, "1234");
        var (nether, _) = await LoadDimensionsAsync(world);
        var level = world.LevelData;
        Assert.Equal(GameMode.Creative, level.DefaultGamemode);
        Assert.Equal(Difficulty.Hard, level.Difficulty);
        Assert.True(level.DifficultyLocked);
        Assert.Equal(5000, level.Time);
        Assert.Equal(gameTime, level.WorldClocks.GameTime);
        Assert.True(level.Raining);
        Assert.Equal(new VectorD(8.5, 70, -7.5), level.SpawnPosition);
        Assert.Equal(1234, level.RandomSeed);
        Assert.True(level.GetBooleanRule("keep_inventory", defaultValue: false));
        Assert.Equal(7, level.GetIntegerRule("random_tick_speed"));
        Assert.Same(level.WorldClocks, nether.LevelData.WorldClocks);
        Assert.Equal(level.Time, nether.LevelData.Time);

        // The dragon fight Obsidian kept is in vanilla's file once the world saves.
        await world.SaveAsync();
        var reloaded = VanillaLevelData.Read(LevelFiles.Load(folder, NullLogger.Instance)!);
        Assert.Equal(level.Time, reloaded.Time);
        Assert.Equal(level.WorldClocks.GameTime, reloaded.WorldClocks.GameTime);

        var fight = SavedDataFile.Read(Path.Combine(folder, "dimensions/minecraft/the_end/data/minecraft/ender_dragon_fight.dat"))!;
        Assert.True(fight.GetBool("dragon_killed"));
        Assert.Equal(18, ((NbtList)fight["gateways"]).Count);

        if (layout == "obsidian")
        {
            Assert.True(File.Exists(Path.Combine(folder, "dimensions/minecraft/overworld/data/sample/networks.dat")));
            Assert.True(File.Exists(Path.Combine(folder, "data/sample/global.dat")));
            Assert.True(File.Exists(Path.Combine(folder, "data/sample/command_storage.dat")));
            Assert.True(File.Exists(Path.Combine(folder, "data/minecraft/maps/3.dat")));
            Assert.True(File.Exists(Path.Combine(folder, "dimensions/sample/glade/region/r.0.0.mca")));
            Assert.True(File.Exists(Path.Combine(folder, $"players/stats/{playerUuid}.json")));
        }
        else
        {
            Assert.Equal(playerUuid, VanillaLevelData.GetSingleplayerOwner(LevelFiles.Load(folder, NullLogger.Instance)!));
        }
    }

    [Fact(DisplayName = "An upgrade that stopped partway is redone from its backup, without another backup")]
    public async Task InterruptedUpgradeIsRedone()
    {
        var folder = Path.Combine(this.root, "world");
        await WriteObsidianWorldAsync(folder);
        await WorldUpgrade.UpgradeAsync(folder, "minecraft:overworld", NullLogger.Instance);
        var upgraded = ReadFiles(folder).Keys.Order().ToArray();

        // A stop partway: the marker names the backup, and the world is half moved.
        var backup = $"{folder}-1.21.11-backup";
        File.WriteAllText(Path.Combine(folder, "obsidian_upgrade_backup.txt"), backup);
        File.Delete(Path.Combine(folder, "level.dat"));
        Directory.Delete(Path.Combine(folder, "players"), recursive: true);

        Assert.True(WorldUpgrade.IsNeeded(folder));
        await WorldUpgrade.UpgradeAsync(folder, "minecraft:overworld", NullLogger.Instance);

        Assert.False(WorldUpgrade.IsNeeded(folder));
        Assert.Equal(upgraded, ReadFiles(folder).Keys.Order());
        Assert.Single(Directory.GetDirectories(this.root, "world-*"));
    }

    [Theory(DisplayName = "A backup's staging folder is reused only when it's a stopped copy of the same world")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackupStagingKeepsOtherFolders(bool stoppedCopy)
    {
        var folder = Path.Combine(this.root, "world");
        await WriteObsidianWorldAsync(folder);
        var original = ReadFiles(folder);

        // A folder at the staging path: a copy of this world that stopped partway, or someone else's world.
        var staging = $"{folder}-1.21.11-backup.partial";
        WriteRoot(Path.Combine(staging, "level.dat"), new NbtCompound { new NbtTag<string>("LevelName", "other") });
        if (stoppedCopy)
            WriteText(Path.Combine(staging, "obsidian_upgrade_staging.txt"), Path.GetFullPath(folder));
        var other = ReadFiles(staging);

        await WorldUpgrade.UpgradeAsync(folder, "minecraft:overworld", NullLogger.Instance);

        if (stoppedCopy)
        {
            Assert.False(Directory.Exists(staging));
            Assert.Equal(original, ReadFiles($"{folder}-1.21.11-backup"));
        }
        else
        {
            Assert.Equal(other, ReadFiles(staging));
            Assert.Equal(original, ReadFiles($"{folder}-1.21.11-backup-2"));
        }
    }

    // Obsidian's own layout, as Obsidian for 1.21.11 saved a server's world.
    private static async Task WriteObsidianWorldAsync(string folder)
    {
        WriteRoot(Path.Combine(folder, "level.dat"), new NbtCompound
        {
            new NbtTag<bool>("hardcore", false),
            new NbtTag<bool>("MapFeatures", true),
            new NbtTag<bool>("raining", true),
            new NbtTag<bool>("thundering", false),
            new NbtTag<int>("GameType", (int)GameMode.Creative),
            new NbtTag<byte>("Difficulty", (byte)Difficulty.Hard),
            new NbtTag<bool>("DifficultyLocked", true),
            new NbtTag<int>("rainTime", 100),
            new NbtTag<int>("SpawnX", 8),
            new NbtTag<int>("SpawnY", 70),
            new NbtTag<int>("SpawnZ", -8),
            new NbtTag<int>("thunderTime", 200),
            new NbtTag<long>("RandomSeed", 1234),
            new NbtTag<long>("Time", 5000),
            new NbtTag<string>("generatorName", "superflat"),
            new NbtTag<string>("LevelName", "legacy"),
            GameRules(),
            new NbtTag<int>("ObsidianTraderChance", 50)
        });

        await WriteRegionAsync(Path.Combine(folder, "regions", "r.0.0.mca"));
        await WriteRegionAsync(Path.Combine(folder, "entities", "r.0.0.mca"));
        WriteRoot(Path.Combine(folder, "playerdata", $"{playerUuid}.dat"), new NbtCompound { new NbtTag<int>("XpLevel", 3) });
        WriteText(Path.Combine(folder, "stats", $"{playerUuid}.json"), "{}");
        WriteText(Path.Combine(folder, "advancements", $"{playerUuid}.json"), "{}");

        WriteSavedData(Path.Combine(folder, "data", "map_3.dat"), new NbtCompound("data") { new NbtTag<byte>("scale", 0) });
        WriteSavedData(Path.Combine(folder, "data", "idcounts.dat"), new NbtCompound("data") { new NbtTag<int>("map", 3) });
        WriteSavedData(Path.Combine(folder, "data", "command_storage_sample.dat"), new NbtCompound("data"));
        WriteSavedData(Path.Combine(folder, "data", "sample", "networks.dat"), new NbtCompound("data"));
        WriteSavedData(Path.Combine(folder, "data", "obsidian", "world", "sample", "global.dat"), new NbtCompound("data"));

        await WriteRegionAsync(Path.Combine(folder, "dimensions", "the_nether", "regions", "r.0.0.mca"));
        WriteRoot(Path.Combine(folder, "dimensions", "the_nether", "level.dat"), DimensionLevel());
        await WriteRegionAsync(Path.Combine(folder, "dimensions", "the_end", "regions", "r.0.0.mca"));
        WriteRoot(Path.Combine(folder, "dimensions", "the_end", "level.dat"), new NbtCompound { EndFight() });
        await WriteRegionAsync(Path.Combine(folder, "dimensions", "sample", "glade", "regions", "r.0.0.mca"));
        WriteRoot(Path.Combine(folder, "dimensions", "sample", "glade", "level.dat"), DimensionLevel());
    }

    // Vanilla 1.21.11's layout, as Obsidian for 1.21.11 saved an integrated server's world.
    private static async Task WriteVanillaWorldAsync(string folder)
    {
        WriteRoot(Path.Combine(folder, "level.dat"), new NbtCompound
        {
            new NbtCompound("Data")
            {
                new NbtTag<string>("LevelName", "legacy"),
                new NbtTag<int>("GameType", (int)GameMode.Creative),
                new NbtTag<byte>("Difficulty", (byte)Difficulty.Hard),
                new NbtTag<byte>("DifficultyLocked", 1),
                new NbtTag<byte>("hardcore", 0),
                new NbtTag<byte>("allowCommands", 1),
                new NbtTag<long>("Time", 9000),
                new NbtTag<long>("DayTime", 5000),
                new NbtTag<byte>("raining", 1),
                new NbtTag<int>("rainTime", 100),
                new NbtTag<int>("DataVersion", 4671),
                new NbtCompound("spawn")
                {
                    new NbtArray<int>("pos", [8, 70, -8]),
                    new NbtTag<string>("dimension", "minecraft:overworld")
                },
                GameRules(),
                new NbtCompound("WorldGenSettings")
                {
                    new NbtTag<long>("seed", 1234),
                    new NbtTag<byte>("generate_features", 1)
                },
                new NbtCompound("Player")
                {
                    new NbtArray<int>("UUID", Obsidian.Entities.EntityNbt.UuidToInts(playerUuid)),
                    new NbtTag<int>("XpLevel", 3)
                }
            },
            new NbtTag<int>("ObsidianTraderChance", 50)
        });

        await WriteRegionAsync(Path.Combine(folder, "region", "r.0.0.mca"));
        await WriteRegionAsync(Path.Combine(folder, "DIM-1", "region", "r.0.0.mca"));
        WriteRoot(Path.Combine(folder, "DIM-1", "level.dat"), DimensionLevel());
        WriteRoot(Path.Combine(folder, "DIM1", "level.dat"), new NbtCompound { EndFight() });
        WriteRoot(Path.Combine(folder, "playerdata", $"{playerUuid}.dat"), new NbtCompound { new NbtTag<int>("XpLevel", 3) });
    }

    private static NbtCompound GameRules() => new("game_rules")
    {
        new NbtTag<bool>("minecraft:keep_inventory", true),
        new NbtTag<int>("minecraft:random_tick_speed", 7)
    };

    // Obsidian's level.dat of a dimension.
    private static NbtCompound DimensionLevel() => new()
    {
        new NbtTag<int>("SpawnX", 0),
        new NbtTag<int>("SpawnY", 64),
        new NbtTag<int>("SpawnZ", 0),
        new NbtTag<long>("Time", 42),
        GameRules()
    };

    private static NbtCompound EndFight() => new("ObsidianEndFight")
    {
        new NbtTag<bool>("Initialized", true),
        new NbtTag<bool>("DragonKilled", true),
        new NbtTag<bool>("PreviouslyKilled", true),
        new NbtTag<int>("PortalY", 64),
        new NbtTag<int>("Gateways", 2)
    };

    private static World CreateWorld(string folder, string seed)
    {
        // No spawn chunks: these tests are about the world's files, not its chunks.
        var configuration = new ServerConfiguration { SpawnChunkRadius = 0 };
        var world = new World(NullLogger<World>.Instance, null!, null!, new TestOptionsMonitor<ServerConfiguration>(configuration),
            null!, new EmptyWorldGenerator(), "world", seed);

        world.UseFolder(folder);
        return world;
    }

    private static async Task<(Dimension Nether, Dimension End)> LoadDimensionsAsync(World world)
    {
        Assert.True(await world.LoadAsync(CodecRegistry.Dimensions.All["minecraft:overworld"]));

        var dimensions = new List<Dimension>();
        foreach (var name in new[] { "minecraft:the_nether", "minecraft:the_end" })
        {
            var codec = CodecRegistry.Dimensions.All[name];
            var dimension = new Dimension(NullLogger<Dimension>.Instance, null!,
                new TestOptionsMonitor<ServerConfiguration>(new ServerConfiguration { SpawnChunkRadius = 0 }), null!,
                new EmptyWorldGenerator(), name, world);
            dimension.Initialize(codec);
            world.RegisterDimension(dimension);
            await dimension.LoadAsync(codec);
            dimensions.Add(dimension);
        }

        return (dimensions[0], dimensions[1]);
    }

    // Every saved data file and level.dat in a world, by path.
    private static Dictionary<string, NbtCompound> ReadDataFiles(string folder) => Directory
        .EnumerateFiles(folder, "*.dat", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(folder, path).Replace('\\', '/'), ReadRoot);

    // Every file in a folder and its contents, by path.
    private static Dictionary<string, string> ReadFiles(string folder) => Directory
        .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .Where(path => Path.GetFileName(path) != "session.lock")
        .ToDictionary(path => Path.GetRelativePath(folder, path).Replace('\\', '/'),
            path => Convert.ToHexString(File.ReadAllBytes(path)));

    // Whether everything in expected is in actual with the same type and value, but the paths Obsidian changes.
    private static void AssertKept(NbtCompound expected, NbtCompound actual, string path, string[] changed)
    {
        foreach (var (name, tag) in expected)
        {
            var at = $"{path}/{name}";
            if (changed.Contains(at))
                continue;

            Assert.True(actual.TryGetTag(name, out var saved), $"{at} is gone");
            if (tag is NbtCompound compound)
                AssertKept(compound, Assert.IsType<NbtCompound>(saved), at, changed);
            else
                Assert.True(Bytes(tag) == Bytes(saved), $"{at} changed");
        }

        static string Bytes(INbtTag tag)
        {
            using var writer = new RawNbtWriter(string.Empty);
            writer.WriteTag(tag);
            writer.EndCompound();
            return Convert.ToHexString(writer.Data);
        }
    }

    private static async Task WriteRegionAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new RegionFile(path, NbtCompression.ZLib);
        await file.InitializeAsync();

        var written = new MemoryStream();
        await using (var writer = new NbtWriterStream(written, NbtCompression.ZLib, ""))
        {
            writer.WriteInt("xPos", 1);
            writer.WriteInt("zPos", 1);
            writer.EndCompound();
            await writer.TryFinishAsync();
        }

        // Not the chunk at the spawn, which loads with the world.
        await file.SetChunkAsync(1, 1, written);
        await file.FlushAsync();
    }

    private static NbtCompound ReadRoot(string path)
    {
        using var stream = File.OpenRead(path);
        return (NbtCompound)new NbtReader(stream, NbtCompression.GZip).ReadNextTag()!;
    }

    private static void WriteRoot(string path, NbtCompound root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new NbtWriterStream(stream, NbtCompression.GZip, "");
        writer.WriteEntries(root);
        writer.EndCompound();
        writer.TryFinish();
    }

    private static void WriteSavedData(string path, NbtCompound data) =>
        WriteRoot(path, new NbtCompound { data, new NbtTag<int>("DataVersion", 4671) });

    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
