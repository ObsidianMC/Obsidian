using Microsoft.Extensions.Logging;
using Obsidian.Nbt;
using System.IO;

namespace Obsidian.WorldData;

/// <summary>
/// A world's level data as vanilla 26.3 splits it: <c>level.dat</c>'s <c>Data</c> compound and the world's saved data in
/// <c>data/minecraft/</c>. Each compound is kept whole as it was read, so what Obsidian doesn't model survives its saves;
/// <see cref="VanillaLevelData.Update"/> overwrites the fields it does.
/// </summary>
internal sealed class LevelFiles
{
    /// <summary><c>level.dat</c>'s <c>Data</c>.</summary>
    public NbtCompound Data { get; init; } = new("Data");

    /// <summary><c>data/minecraft/game_rules.dat</c>: the rules by registry id.</summary>
    public NbtCompound GameRules { get; init; } = new("data");

    /// <summary><c>data/minecraft/weather.dat</c>.</summary>
    public NbtCompound Weather { get; init; } = new("data");

    /// <summary><c>data/minecraft/world_clocks.dat</c>: each clock's <c>total_ticks</c>, by clock id.</summary>
    public NbtCompound WorldClocks { get; init; } = new("data");

    /// <summary><c>data/minecraft/world_gen_settings.dat</c>: the seed and each dimension's generator.</summary>
    public NbtCompound WorldGenSettings { get; init; } = new("data");

    /// <summary><c>data/minecraft/wandering_trader.dat</c>: the overworld's trader spawn delay and chance.</summary>
    public NbtCompound WanderingTrader { get; init; } = new("data");

    // The saved data files by id, which Load reads them from.
    private IEnumerable<(string Id, NbtCompound Data)> SavedData =>
    [
        ("minecraft:game_rules", this.GameRules),
        ("minecraft:weather", this.Weather),
        ("minecraft:world_clocks", this.WorldClocks),
        ("minecraft:world_gen_settings", this.WorldGenSettings),
        ("minecraft:wandering_trader", this.WanderingTrader)
    ];

    /// <summary>
    /// The level data of the world in <paramref name="worldFolder"/>, or <c>null</c> when it has no level.dat, so the
    /// world is new. A saved data file that's missing reads as empty.
    /// </summary>
    /// <exception cref="InvalidDataException">The level data or a saved data file can't be read.</exception>
    public static LevelFiles? Load(string worldFolder, ILogger logger)
    {
        var root = World.ReadLevelDataOrBackup(Path.Combine(worldFolder, WorldLayout.LevelDataFile), logger);
        if (root is null)
            return null;

        if (!root.TryGetTag<NbtCompound>("Data", out var data))
            throw new InvalidDataException($"The level.dat in {worldFolder} has no Data.");

        return Load(worldFolder, data);
    }

    /// <summary>
    /// The level data of the world in <paramref name="worldFolder"/> with <paramref name="data"/> as level.dat's
    /// <c>Data</c>, and its saved data files as they are. A saved data file that's missing reads as empty.
    /// </summary>
    /// <exception cref="InvalidDataException">A saved data file can't be read.</exception>
    public static LevelFiles Load(string worldFolder, NbtCompound data)
    {
        return new LevelFiles
        {
            Data = data,
            GameRules = Read("game_rules"),
            Weather = Read("weather"),
            WorldClocks = Read("world_clocks"),
            WorldGenSettings = Read("world_gen_settings"),
            WanderingTrader = Read("wandering_trader")
        };

        NbtCompound Read(string name) =>
            SavedDataFile.Read(WorldLayout.SavedDataPath(worldFolder, $"minecraft:{name}")) ?? new("data");
    }

    /// <summary>
    /// Writes the saved data files, then level.dat, which goes to a temporary file first and then replaces level.dat,
    /// keeping the previous one as vanilla's <c>level.dat_old</c>, so a save that stops partway leaves a complete file.
    /// </summary>
    public void Save(string worldFolder)
    {
        foreach (var (id, compound) in this.SavedData)
            SavedDataFile.Write(WorldLayout.SavedDataPath(worldFolder, id), compound);

        var path = Path.Combine(worldFolder, WorldLayout.LevelDataFile);
        var temporaryPath = $"{path}.tmp";
        using (var stream = File.Create(temporaryPath))
        using (var writer = new NbtWriterStream(stream, NbtCompression.GZip, ""))
        {
            this.Data.Name = "Data";
            writer.WriteTag(this.Data);
            writer.EndCompound();
            writer.TryFinish();
        }

        if (File.Exists(path))
            File.Replace(temporaryPath, path, $"{path}_old");
        else
            File.Move(temporaryPath, path);
    }
}
