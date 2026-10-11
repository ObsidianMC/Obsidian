using System.IO;
using System.IO.Compression;

namespace Obsidian.Plugins.Archives;

/// <summary>
/// A version 2 plugin archive: a ZIP file with <c>obsidian.mod.json</c> at its root, the plugin's assemblies beside it,
/// and optional <c>assets/</c> and <c>data/</c> folders.
/// See docs/plugin-archives.md for the format.
/// </summary>
internal sealed class ZipPluginArchive : PluginArchive
{
    private readonly ZipArchive zip;
    private readonly Dictionary<string, ZipArchiveEntry> entries;

    private ZipPluginArchive(ZipArchive zip, Dictionary<string, ZipArchiveEntry> entries)
    {
        this.zip = zip;
        this.entries = entries;
    }

    public override IEnumerable<string> Paths => this.entries.Keys;

    public static bool IsZip(ReadOnlySpan<byte> data) => data.StartsWith("PK\x03\x04"u8);

    public static ZipPluginArchive Open(string source, byte[] data)
    {
        var zip = new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read);

        try
        {
            var entries = ReadEntries(zip);

            var metadata = entries.TryGetValue(PluginMetadata.FileName, out var metadataEntry)
                ? PluginMetadata.Parse(ReadEntry(metadataEntry))
                : throw new InvalidDataException($"The archive has no {PluginMetadata.FileName}.");

            return new ZipPluginArchive(zip, entries)
            {
                Source = source,
                Info = metadata.ToPluginInfo()
            };
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    public override byte[]? Read(string path)
    {
        if (!this.entries.TryGetValue(path, out var entry))
            return null;

        // A ZipArchive reads from one shared stream, so entries are read one at a time.
        lock (this.zip)
        {
            return ReadEntry(entry);
        }
    }

    private static Dictionary<string, ZipArchiveEntry> ReadEntries(ZipArchive zip)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);

        foreach (var entry in zip.Entries)
        {
            // Folders have entries of their own in some ZIP files; only files matter.
            if (entry.FullName.EndsWith('/'))
                continue;

            // Asset and data paths are later used to find files on disk too, so none may leave the archive.
            if (entry.FullName.Contains('\\')
                || entry.FullName.StartsWith('/')
                || entry.FullName.Split('/').Contains(".."))
                throw new InvalidDataException($"The archive has an invalid path '{entry.FullName}'.");

            if (!entries.TryAdd(entry.FullName, entry))
                throw new InvalidDataException($"The archive has '{entry.FullName}' more than once.");
        }

        return entries;
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        var data = new byte[entry.Length];

        stream.ReadExactly(data);

        return data;
    }
}
