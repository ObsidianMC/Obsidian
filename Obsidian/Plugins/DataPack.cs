using Microsoft.Extensions.Logging;
using Obsidian.Plugins.Archives;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Obsidian.Plugins;

/// <summary>
/// The data files of a plugin archive or a data pack.
/// </summary>
/// <param name="Name">What messages call it: a plugin's id, or a data pack's file or folder name.</param>
/// <param name="Location">Where it was loaded from.</param>
/// <param name="Paths">Its files' paths, with <c>/</c> separators.</param>
/// <param name="Read">Reads a file by its path, or gives null for a path it doesn't have.</param>
internal sealed record DataFiles(
    string Name,
    string Location,
    IReadOnlyCollection<string> Paths,
    Func<string, byte[]?> Read)
{
    public static DataFiles Of(PluginArchive archive) =>
        new(archive.Info.Id, archive.Source, [.. archive.Paths], archive.Read);
}

/// <summary>
/// A vanilla data pack from the server's data packs folder (<see cref="ServerConstants.DataPacksPath"/>): a folder or a
/// zip file with a <c>pack.mcmeta</c> at its root. Its <c>data/</c> files are read like a plugin's, once at startup,
/// and applied after every plugin's, in the packs' name order, so that the last pack wins, like vanilla's.
/// </summary>
/// <remarks>
/// Only the files Obsidian reads from plugins are applied (worldgen, biomes, dimension and damage types, and tags);
/// others, like recipes or structures, are left out. A pack's <c>filter</c> and <c>overlays</c> aren't applied.
/// </remarks>
internal sealed partial class DataPack(DataFiles files)
{
    private const string Metadata = "pack.mcmeta";

    // Gson reads pack.mcmeta leniently, so comments and trailing commas are fine.
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// The pack's files.
    /// </summary>
    public DataFiles Files { get; } = files;

    /// <summary>
    /// The pack's tag files, merged with vanilla's, Obsidian's and plugins' tags after plugins' files.
    /// </summary>
    public List<TagFile> TagFiles { get; } = [];

    /// <summary>
    /// The pack's biome, dimension type and damage type files.
    /// </summary>
    public ImmutableArray<CodecFile> CodecFiles { get; set; } = [];

    /// <summary>
    /// The pack's feature, carver and biome modifier files.
    /// </summary>
    public ImmutableArray<FeatureFile> FeatureFiles { get; set; } = [];

    /// <summary>
    /// The pack's noise settings, density function, noise and parameter list files.
    /// </summary>
    public ImmutableArray<NoiseFile> NoiseFiles { get; set; } = [];

    /// <summary>
    /// The pack's level stem files.
    /// </summary>
    public ImmutableArray<DimensionFile> DimensionFiles { get; set; } = [];

    /// <summary>
    /// Opens the data packs in a folder, in name order. A folder or zip file without a valid <c>pack.mcmeta</c> is
    /// logged and left out, like vanilla leaves out a pack it can't read.
    /// </summary>
    public static List<DataPack> OpenAll(string folder, ILogger logger)
    {
        if (!Directory.Exists(folder))
            return [];

        var packs = new List<DataPack>();

        foreach (var path in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal))
        {
            try
            {
                if (Open(path, logger) is DataFiles opened)
                    packs.Add(new DataPack(opened));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Log.OpenFailed(logger, Path.GetFileName(path), ex.Message);
            }
        }

        return packs;
    }

    // A folder or zip file holding a pack, or null for another file.
    private static DataFiles? Open(string path, ILogger logger)
    {
        var name = Path.GetFileName(path);
        DataFiles files;

        if (Directory.Exists(path))
        {
            files = OpenFolder(name, path);
        }
        else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            files = OpenZip(name, path);
        }
        else
        {
            Log.NotAPack(logger, name);
            return null;
        }

        var metadata = files.Read(Metadata) ?? throw new InvalidDataException($"{name} has no {Metadata}.");
        CheckMetadata(name, metadata, logger);

        return files;
    }

    private static DataFiles OpenFolder(string name, string folder)
    {
        var paths = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        return new DataFiles(
            name,
            folder,
            paths,
            path => paths.Contains(path) ? File.ReadAllBytes(Path.Join(folder, path)) : null);
    }

    // A zip's metadata and data files are read now, so the file can change while the server runs.
    private static DataFiles OpenZip(string name, string file)
    {
        using var zip = ZipFile.OpenRead(file);
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var entry in zip.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');
            if (path.EndsWith('/') || path != Metadata && !path.StartsWith("data/", StringComparison.Ordinal))
                continue;

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            contents[path] = memory.ToArray();
        }

        return new DataFiles(name, file, contents.Keys, path => contents.GetValueOrDefault(path));
    }

    // Vanilla requires a pack object; what Obsidian doesn't apply is logged once per pack.
    private static void CheckMetadata(string name, byte[] metadata, ILogger logger)
    {
        try
        {
            using var document = JsonDocument.Parse(metadata, documentOptions);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("pack", out var pack)
                || pack.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{name}'s {Metadata} has no pack object.");
            }

            foreach (var section in (ReadOnlySpan<string>)["filter", "overlays"])
            {
                if (root.TryGetProperty(section, out _))
                    Log.Unsupported(logger, name, section);
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{name}'s {Metadata} isn't valid JSON: {ex.Message}", ex);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Left out data pack {Name}: {Message}")]
        public static partial void OpenFailed(ILogger logger, string name, string message);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Left out {Name} in the data packs folder: a data pack is a folder or a zip file")]
        public static partial void NotAPack(ILogger logger, string name);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Data pack {Name} has a {Section} section, which Obsidian doesn't apply")]
        public static partial void Unsupported(ILogger logger, string name, string section);
    }
}
