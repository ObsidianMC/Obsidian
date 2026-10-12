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
/// How much of a data pack is read, so that a pack, like a zip bomb, can't exhaust the server's memory; vanilla has no
/// limits. A pack over them is left out.
/// </summary>
/// <param name="Files">The most files a pack's <c>data/</c> folder (or a zip file, entries) may have.</param>
/// <param name="FileSize">The most bytes a file read may have, decompressed.</param>
/// <param name="TotalSize">The most bytes the files read may have together, decompressed.</param>
internal sealed record DataPackLimits(int Files, long FileSize, long TotalSize)
{
    /// <summary>
    /// Limits well above large worldgen packs: their biggest files, level stems with inline biome lists, are a few
    /// megabytes.
    /// </summary>
    public static DataPackLimits Default { get; } = new(65_536, 32L << 20, 512L << 20);
}

/// <summary>
/// A vanilla data pack from the server's data packs folder (<see cref="ServerConstants.DataPacksPath"/>): a folder or a
/// zip file with a <c>pack.mcmeta</c> at its root. Its <c>data/</c> files are read like a plugin's, once at startup,
/// and applied after every plugin's, in the packs' name order, so that the last pack wins, like vanilla's.
/// </summary>
/// <remarks>
/// Only the files Obsidian reads from plugins are applied (worldgen, biomes, dimension and damage types, and tags);
/// others, like recipes or structures, are left out unread. A pack's <c>filter</c> and <c>overlays</c> aren't applied.
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
    public DataFiles Files { get; private set; } = files;

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
    /// Opens the data packs in a folder, in name order. A folder or zip file without a valid <c>pack.mcmeta</c>, or
    /// over the <paramref name="limits"/>, is logged and left out, like vanilla leaves out a pack it can't read.
    /// </summary>
    /// <param name="reads">Whether Obsidian reads a <c>data/</c> file, by its path. Only those files are read, now,
    /// so that a pack can change while the server runs; the others' paths are kept, to count them.</param>
    /// <param name="limits">How much of a pack is read; <see cref="DataPackLimits.Default"/> by default.</param>
    public static List<DataPack> OpenAll(
        string folder,
        Func<string, bool> reads,
        ILogger logger,
        DataPackLimits? limits = null)
    {
        if (!Directory.Exists(folder))
            return [];

        limits ??= DataPackLimits.Default;
        var packs = new List<DataPack>();

        foreach (var path in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal))
        {
            try
            {
                if (Open(path, reads, limits, logger) is DataFiles opened)
                    packs.Add(new DataPack(opened));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Log.OpenFailed(logger, Path.GetFileName(path), ex.Message);
            }
        }

        return packs;
    }

    /// <summary>
    /// Lets go of the files' contents once they're read, keeping their paths.
    /// </summary>
    public void DropContents() => this.Files = this.Files with { Read = static _ => null };

    // A folder or zip file holding a pack, or null for another file.
    private static DataFiles? Open(string path, Func<string, bool> reads, DataPackLimits limits, ILogger logger)
    {
        var name = Path.GetFileName(path);

        if (Directory.Exists(path))
            return OpenFolder(name, path, reads, limits, logger);

        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return OpenZip(name, path, reads, limits, logger);

        Log.NotAPack(logger, name);
        return null;
    }

    // Symbolic links and junctions in the pack are skipped, so that what's read stays in its folder.
    private static DataFiles OpenFolder(
        string name,
        string folder,
        Func<string, bool> reads,
        DataPackLimits limits,
        ILogger logger)
    {
        var metadata = new FileInfo(Path.Join(folder, Metadata));
        if (!metadata.Exists || metadata.LinkTarget is not null)
            throw new InvalidDataException($"{name} has no {Metadata}.");

        using (var stream = metadata.OpenRead())
            CheckMetadata(name, ReadBounded(stream, Metadata, limits.FileSize), logger);

        var contents = new PackContents(name, limits);
        var data = new DirectoryInfo(Path.Join(folder, "data"));

        if (data.Exists && data.LinkTarget is null)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var file in data.EnumerateFiles("*", options))
            {
                var path = Path.GetRelativePath(folder, file.FullName).Replace('\\', '/');
                if (!contents.Add(path, reads))
                    continue;

                using var stream = file.OpenRead();
                contents.Read(path, stream, file.Length);
            }
        }

        return contents.ToFiles(folder);
    }

    // The metadata is checked before any data file is read.
    private static DataFiles OpenZip(
        string name,
        string file,
        Func<string, bool> reads,
        DataPackLimits limits,
        ILogger logger)
    {
        using var zip = ZipFile.OpenRead(file);
        if (zip.Entries.Count > limits.Files)
            throw new InvalidDataException($"{name} has more than {limits.Files} files.");

        var metadata = zip.GetEntry(Metadata) ?? throw new InvalidDataException($"{name} has no {Metadata}.");
        if (metadata.Length > limits.FileSize)
            throw new InvalidDataException($"{name}'s {Metadata} is larger than {limits.FileSize} bytes.");

        using (var stream = metadata.Open())
            CheckMetadata(name, ReadBounded(stream, Metadata, limits.FileSize), logger);

        var contents = new PackContents(name, limits);

        foreach (var entry in zip.Entries)
        {
            // Folders have entries of their own in some zip files; only files matter.
            var path = entry.FullName;
            if (path.EndsWith('/') || !path.StartsWith("data/", StringComparison.Ordinal))
                continue;

            // Like a plugin archive's paths, none may leave the pack.
            if (path.Contains('\\') || path.Split('/').Contains(".."))
                throw new InvalidDataException($"{name} has an invalid path '{path}'.");

            if (!contents.Add(path, reads))
                continue;

            using var stream = entry.Open();
            contents.Read(path, stream, entry.Length);
        }

        return contents.ToFiles(file);
    }

    // Reads a stream to its end, failing once it has more than the limit: a zip entry's stated size can't be trusted.
    private static byte[] ReadBounded(Stream stream, string path, long limit)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[81_920];
        int read;

        while ((read = stream.Read(buffer)) > 0)
        {
            if (memory.Length + read > limit)
                throw new InvalidDataException($"{path} is larger than {limit} bytes.");

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
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

    // A pack's data files as they're read, within its limits.
    private sealed class PackContents(string name, DataPackLimits limits)
    {
        private readonly HashSet<string> paths = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> read = new(StringComparer.Ordinal);
        private long size;

        // Counts a file, and gives whether it's to be read.
        public bool Add(string path, Func<string, bool> reads)
        {
            if (!this.paths.Add(path))
                throw new InvalidDataException($"{name} has '{path}' more than once.");

            if (this.paths.Count > limits.Files)
                throw new InvalidDataException($"{name} has more than {limits.Files} files.");

            return reads(path);
        }

        public void Read(string path, Stream stream, long length)
        {
            if (length > limits.FileSize)
                throw new InvalidDataException($"{name}: {path} is larger than {limits.FileSize} bytes.");

            var data = ReadBounded(stream, $"{name}: {path}", limits.FileSize);

            this.size += data.Length;
            if (this.size > limits.TotalSize)
                throw new InvalidDataException($"{name}'s files are larger than {limits.TotalSize} bytes together.");

            this.read[path] = data;
        }

        public DataFiles ToFiles(string location) =>
            new(name, location, this.paths, path => this.read.GetValueOrDefault(path));
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
