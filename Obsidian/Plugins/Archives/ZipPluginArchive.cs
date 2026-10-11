using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.Plugins.Archives;

/// <summary>
/// A version 2 plugin archive: a ZIP file with <c>obsidian.mod.json</c> at its root, the plugin's assemblies beside it,
/// optional <c>assets/</c> and <c>data/</c> folders, and an optional <c>obsidian.sig</c> signature.
/// See docs/plugin-archives.md for the format.
/// </summary>
internal sealed class ZipPluginArchive : PluginArchive
{
    public const string SignatureFileName = "obsidian.sig";

    private readonly ZipArchive zip;
    private readonly Dictionary<string, ZipArchiveEntry> entries;

    private ZipPluginArchive(ZipArchive zip, Dictionary<string, ZipArchiveEntry> entries)
    {
        this.zip = zip;
        this.entries = entries;
    }

    public override IEnumerable<string> Paths => this.entries.Keys;

    public static bool IsZip(ReadOnlySpan<byte> data) => data.StartsWith("PK\x03\x04"u8);

    public static ZipPluginArchive Open(string source, byte[] data, IReadOnlyList<RSAParameters> acceptedKeys)
    {
        var zip = new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read);

        try
        {
            var entries = ReadEntries(zip);

            var metadata = entries.TryGetValue(PluginMetadata.FileName, out var metadataEntry)
                ? PluginMetadata.Parse(ReadEntry(metadataEntry))
                : throw new InvalidDataException($"The archive has no {PluginMetadata.FileName}.");

            var signature = entries.TryGetValue(SignatureFileName, out var signatureEntry)
                ? ReadEntry(signatureEntry)
                : null;

            var validSignature = signature is not null
                && VerifySignature(ComputeDigest(entries.Values), signature, acceptedKeys);

            return new ZipPluginArchive(zip, entries)
            {
                Source = source,
                Info = metadata.ToPluginInfo(),
                ValidSignature = validSignature
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

    /// <summary>
    /// The digest an archive's signature signs: SHA-384 over every file except the signature, in ordinal order of
    /// their paths, each contributing its UTF-8 path, a zero byte and the SHA-384 hash of its contents. It depends only
    /// on the files, not on how the ZIP file stores them.
    /// </summary>
    internal static byte[] ComputeDigest(IEnumerable<ZipArchiveEntry> entries)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA384);

        foreach (var entry in entries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            if (entry.FullName == SignatureFileName)
                continue;

            digest.AppendData(Encoding.UTF8.GetBytes(entry.FullName));
            digest.AppendData([0]);
            digest.AppendData(SHA384.HashData(ReadEntry(entry)));
        }

        return digest.GetHashAndReset();
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
