using Microsoft.Extensions.Logging;
using Obsidian.Plugins.Archives;
using System.IO;
using System.IO.Compression;

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
    /// <summary>
    /// What's left of the files' size limits as they're read; a data pack's counts the files read when it opened.
    /// </summary>
    public DataBudget Budget { get; init; } = new(Name, DataPackLimits.Default);

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
/// What's left of a plugin's or data pack's <see cref="DataPackLimits"/> as its files are read. Each file counts once,
/// with the largest size read of it, so a structure template counts with its NBT decompressed rather than its gzipped
/// bytes. Not thread-safe: a plugin's or pack's files are read one after another.
/// </summary>
internal sealed class DataBudget(string name, DataPackLimits limits)
{
    private readonly Dictionary<string, long> charged = new(StringComparer.Ordinal);
    private long used;

    public DataPackLimits Limits => limits;

    /// <summary>
    /// The most bytes a file may have: its own limit, or what's left of the total with what the file counts for.
    /// </summary>
    public long Available(string path) =>
        Math.Min(limits.FileSize, limits.TotalSize - this.used + this.charged.GetValueOrDefault(path));

    /// <summary>
    /// Counts a file with <paramref name="size"/> bytes, unless it counts for more already.
    /// </summary>
    /// <exception cref="InvalidDataException">The files are larger than the total together.</exception>
    public void Charge(string path, long size)
    {
        var previous = this.charged.GetValueOrDefault(path);
        if (size <= previous)
            return;

        if (this.used - previous + size > limits.TotalSize)
            throw new InvalidDataException($"{name}'s files are larger than {limits.TotalSize} bytes together.");

        this.used += size - previous;
        this.charged[path] = size;
    }
}

/// <summary>
/// A vanilla data pack from the server's data packs folder (<see cref="ServerConstants.DataPacksPath"/>): a folder or a
/// zip file with a <c>pack.mcmeta</c> at its root. Its <c>data/</c> files are read like a plugin's, once at startup,
/// and applied after every plugin's, in the packs' name order, so that the last pack wins, like vanilla's.
/// </summary>
/// <remarks>
/// Only the files Obsidian reads from plugins are applied (worldgen, structures and their templates, biomes, dimension
/// and damage types, and tags); others, like recipes or loot tables, are left out unread. Like vanilla, a pack's
/// <c>overlays</c> for the current data pack format are applied over its root, and its <c>filter</c> blocks files
/// of the packs before it (see <see cref="DataPackMetadata"/>); plugins' files and vanilla's own entries aren't
/// filtered.
/// </remarks>
internal sealed partial class DataPack(DataFiles files)
{
    private const string Metadata = "pack.mcmeta";

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
    /// The pack's structure, structure set and template pool files.
    /// </summary>
    public ImmutableArray<StructureFile> StructureFiles { get; set; } = [];

    /// <summary>
    /// The pack's structure templates.
    /// </summary>
    public ImmutableArray<TemplateFile> StructureTemplates { get; set; } = [];

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
                if (Open(path, reads, limits, logger) is (DataFiles opened, DataPackMetadata metadata))
                {
                    // Like vanilla's, a pack's filter blocks the files of the packs before it, not its own.
                    foreach (var before in packs)
                        before.Block(metadata, logger);

                    packs.Add(new DataPack(opened));
                }
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

    // Leaves out the files a later pack's filter blocks.
    private void Block(DataPackMetadata metadata, ILogger logger)
    {
        var blocked = this.Files.Paths.Where(metadata.Blocks).ToHashSet(StringComparer.Ordinal);
        if (blocked.Count == 0)
            return;

        var read = this.Files.Read;

        this.Files = this.Files with
        {
            Paths = [.. this.Files.Paths.Where(path => !blocked.Contains(path))],
            Read = path => blocked.Contains(path) ? null : read(path)
        };

        Log.Blocked(logger, this.Files.Name, blocked.Count);
    }

    // A folder or zip file holding a pack, with its metadata, or null for another file.
    private static (DataFiles, DataPackMetadata)? Open(
        string path,
        Func<string, bool> reads,
        DataPackLimits limits,
        ILogger logger)
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
    private static (DataFiles, DataPackMetadata) OpenFolder(
        string name,
        string folder,
        Func<string, bool> reads,
        DataPackLimits limits,
        ILogger logger)
    {
        var metadata = new FileInfo(Path.Join(folder, Metadata));
        if (!metadata.Exists || metadata.LinkTarget is not null)
            throw new InvalidDataException($"{name} has no {Metadata}.");

        DataPackMetadata packMetadata;
        using (var stream = metadata.OpenRead())
            packMetadata = DataPackMetadata.Read(name, ReadBounded(stream, Metadata, limits.FileSize), logger);

        var contents = new PackContents(name, limits);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var layer in Layers(packMetadata))
        {
            var root = new DirectoryInfo(Path.Join(folder, layer));
            var data = new DirectoryInfo(Path.Join(root.FullName, "data"));
            if (!data.Exists || data.LinkTarget is not null || layer.Length > 0 && root.LinkTarget is not null)
                continue;

            foreach (var file in data.EnumerateFiles("*", options))
            {
                var stored = Path.GetRelativePath(folder, file.FullName).Replace('\\', '/');
                var path = Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');
                if (!contents.Add(stored, path, reads))
                    continue;

                using var stream = file.OpenRead();
                contents.Read(path, stream, file.Length);
            }
        }

        return (contents.ToFiles(folder), packMetadata);
    }

    // The folders whose data/ files a pack holds, those whose files win first: its overlays from the last listed,
    // then its root, like vanilla's composite pack resources.
    private static IEnumerable<string> Layers(DataPackMetadata metadata) =>
        Enumerable.Reverse(metadata.Overlays).Append("");

    // The metadata is checked before any data file is read.
    private static (DataFiles, DataPackMetadata) OpenZip(
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

        DataPackMetadata packMetadata;
        using (var stream = metadata.Open())
            packMetadata = DataPackMetadata.Read(name, ReadBounded(stream, Metadata, limits.FileSize), logger);

        var contents = new PackContents(name, limits);

        foreach (var layer in Layers(packMetadata))
        {
            var prefix = layer.Length == 0 ? "data/" : $"{layer}/data/";

            foreach (var entry in zip.Entries)
            {
                // Folders have entries of their own in some zip files; only files matter.
                var stored = entry.FullName;
                if (stored.EndsWith('/') || !stored.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                // Like a plugin archive's paths, none may leave the pack.
                if (stored.Contains('\\') || stored.Split('/').Contains(".."))
                    throw new InvalidDataException($"{name} has an invalid path '{stored}'.");

                var path = stored[(prefix.Length - "data/".Length)..];
                if (!contents.Add(stored, path, reads))
                    continue;

                using var stream = entry.Open();
                contents.Read(path, stream, entry.Length);
            }
        }

        return (contents.ToFiles(file), packMetadata);
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

    // A pack's data files as they're read, within its limits. Every file stored counts, overlays' included.
    private sealed class PackContents(string name, DataPackLimits limits)
    {
        private readonly HashSet<string> stored = new(StringComparer.Ordinal);
        private readonly HashSet<string> paths = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> read = new(StringComparer.Ordinal);
        private readonly DataBudget budget = new(name, limits);

        // Counts a file stored at a path in the pack, which is the file at path (data/...) unless an overlay added
        // before holds that path, and gives whether it's to be read.
        public bool Add(string storedPath, string path, Func<string, bool> reads)
        {
            if (!this.stored.Add(storedPath))
                throw new InvalidDataException($"{name} has '{storedPath}' more than once.");

            if (this.stored.Count > limits.Files)
                throw new InvalidDataException($"{name} has more than {limits.Files} files.");

            return this.paths.Add(path) && reads(path);
        }

        public void Read(string path, Stream stream, long length)
        {
            if (length > limits.FileSize)
                throw new InvalidDataException($"{name}: {path} is larger than {limits.FileSize} bytes.");

            var data = ReadBounded(stream, $"{name}: {path}", limits.FileSize);
            this.budget.Charge(path, data.Length);
            this.read[path] = data;
        }

        public DataFiles ToFiles(string location) =>
            new(name, location, this.paths, path => this.read.GetValueOrDefault(path)) { Budget = this.budget };
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Left out data pack {Name}: {Message}")]
        public static partial void OpenFailed(ILogger logger, string name, string message);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Left out {Name} in the data packs folder: a data pack is a folder or a zip file")]
        public static partial void NotAPack(ILogger logger, string name);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "A later data pack's filter left out {Count} files of data pack {Name}")]
        public static partial void Blocked(ILogger logger, string name, int count);
    }
}
