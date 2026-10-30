using Obsidian.Nbt;
using System.IO;

namespace Obsidian.WorldData;

/// <summary>
/// Where vanilla 26.3 keeps a world's files, and its saved data files. Every Obsidian world is laid out like this,
/// whether it's a server's world in <c>worlds/&lt;name&gt;</c> or an integrated server's save.
/// </summary>
/// <remarks>
/// A world's folder holds <c>level.dat</c>, the world's saved data in <c>data/&lt;namespace&gt;/</c> (vanilla's game
/// rules, weather, clocks and world generation settings in <c>data/minecraft/</c>, maps in <c>data/minecraft/maps/</c>),
/// players in <c>players/{data,stats,advancements}/</c>, and every level, the overworld too, in
/// <c>dimensions/&lt;namespace&gt;/&lt;path&gt;/</c> with its <c>region/</c>, <c>entities/</c>, <c>poi/</c> and its own
/// saved data in <c>data/</c>. What only Obsidian keeps goes in <c>data/obsidian/</c>, which vanilla ignores.
/// </remarks>
internal static class WorldLayout
{
    public const string LevelDataFile = "level.dat";

    public const string RegionFolder = "region";

    public static readonly string PlayerDataFolder = Path.Combine("players", "data");

    /// <summary>
    /// A level's folder in its world's folder: <c>dimensions/&lt;namespace&gt;/&lt;path&gt;</c>, like vanilla's
    /// <c>DimensionType.getStorageFolder</c> since 26.1 (the overworld, nether and end included).
    /// </summary>
    public static string LevelFolder(string dimension)
    {
        var id = dimension.Contains(':') ? dimension : $"minecraft:{dimension}";
        var parts = id.Split(':', 2);

        return Path.Combine("dimensions", parts[0], parts[1].Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// The file of saved data <paramref name="identifier"/> in a world's or level's folder:
    /// <c>data/&lt;namespace&gt;/&lt;path&gt;.dat</c>.
    /// </summary>
    public static string SavedDataPath(string folder, string identifier)
    {
        var parts = identifier.Split(':', 2);
        return Path.Combine(folder, "data", parts[0], parts[1].Replace('/', Path.DirectorySeparatorChar) + ".dat");
    }
}

/// <summary>
/// Vanilla's saved data files: a gzipped root holding the payload in <c>data</c> and the <c>DataVersion</c> that saved it.
/// </summary>
internal static class SavedDataFile
{
    /// <summary>
    /// The payload of the file at <paramref name="path"/>, or <c>null</c> when there's no file.
    /// </summary>
    /// <exception cref="InvalidDataException">The file isn't saved data.</exception>
    public static NbtCompound? Read(string path)
    {
        if (!File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        var root = new NbtReader(stream, NbtCompression.GZip).ReadNextTag() as NbtCompound;

        return root is not null && root.TryGetTag<NbtCompound>("data", out var data)
            ? data
            : throw new InvalidDataException($"{path} isn't saved data.");
    }

    /// <summary>
    /// Writes <paramref name="data"/> as the payload of the file at <paramref name="path"/>, through a temporary file,
    /// so a save that stops partway leaves the previous file whole.
    /// </summary>
    public static void Write(string path, NbtCompound data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.tmp";

        data.Name = "data";
        using (var stream = File.Create(temporary))
        using (var writer = new NbtWriterStream(stream, NbtCompression.GZip, ""))
        {
            writer.WriteTag(data);
            writer.WriteInt("DataVersion", ServerConstants.DataVersion);
            writer.EndCompound();
            writer.TryFinish();
        }

        File.Move(temporary, path, overwrite: true);
    }
}
