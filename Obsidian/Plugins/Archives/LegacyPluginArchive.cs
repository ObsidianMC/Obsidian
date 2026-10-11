using Obsidian.API.Plugins;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Obsidian.Plugins.Archives;

/// <summary>
/// A version 1 plugin archive, as packed by Obsidian.MSBuild: an <c>OBBY</c> header, a SHA-384 hash and optional
/// signature, the plugin's metadata, then a table of files that are each deflated or stored. Its file names are flat.
/// </summary>
/// <remarks>
/// If the archive contains <c>obsidian.mod.json</c> (the packer packs every file of the publish folder), its metadata
/// is used instead of the header's, which gives version 1 archives the dependency types, ordering and side of version
/// 2.
/// </remarks>
internal sealed class LegacyPluginArchive : PluginArchive
{
    private readonly Dictionary<string, Entry> entries;

    private LegacyPluginArchive(Dictionary<string, Entry> entries)
    {
        this.entries = entries;
    }

    public override IEnumerable<string> Paths => this.entries.Keys;

    public static bool IsLegacy(ReadOnlySpan<byte> data) => data.StartsWith("OBBY"u8);

    public static LegacyPluginArchive Open(string source, byte[] data, IReadOnlyList<RSAParameters> acceptedKeys)
    {
        using var reader = new BinaryReader(new MemoryStream(data, writable: false));

        reader.ReadBytes(4);

        var apiVersion = reader.ReadString();
        var hash = reader.ReadBytes(SHA384.HashSizeInBytes);
        var signature = reader.ReadBoolean() ? reader.ReadBytes(reader.ReadInt32()) : null;

        // The data length the packer writes isn't needed: the hash covers everything from here to the end.
        reader.ReadInt32();

        var dataStart = (int)reader.BaseStream.Position;

        if (!SHA384.HashData(data.AsSpan(dataStart)).AsSpan().SequenceEqual(hash))
            throw new InvalidDataException("The archive's contents don't match its hash.");

        var headerInfo = ReadInfo(reader, apiVersion);
        var entries = ReadEntries(reader, data);

        var info = entries.TryGetValue(PluginMetadata.FileName, out var metadata)
            ? PluginMetadata.Parse(ReadEntry(metadata)).ToPluginInfo()
            : headerInfo;

        return new LegacyPluginArchive(entries)
        {
            Source = source,
            Info = info,
            ValidSignature = VerifySignature(hash, signature, acceptedKeys)
        };
    }

    public override byte[]? Read(string path) =>
        this.entries.TryGetValue(path, out var entry) ? ReadEntry(entry) : null;

    private static byte[] ReadEntry(Entry entry)
    {
        var stored = new MemoryStream(entry.Archive, entry.Offset, entry.StoredLength, writable: false);

        if (entry.StoredLength == entry.Length)
            return stored.ToArray();

        using var deflate = new DeflateStream(stored, CompressionMode.Decompress);
        var contents = new byte[entry.Length];

        deflate.ReadExactly(contents);

        return contents;
    }

    private static PluginInfo ReadInfo(BinaryReader reader, string apiVersion)
    {
        var assembly = reader.ReadString();
        var version = reader.ReadString();
        var name = reader.ReadString();
        var id = reader.ReadString();
        var authors = reader.ReadString();
        var description = reader.ReadString();
        var projectUrl = reader.ReadString();

        var dependencies = new PluginDependency[reader.ReadInt32()];
        for (var i = 0; i < dependencies.Length; i++)
        {
            dependencies[i] = new PluginDependency
            {
                Id = reader.ReadString(),
                Version = reader.ReadString(),
                Required = reader.ReadBoolean()
            };
        }

        return new PluginInfo
        {
            Id = id,
            Name = name,
            Version = Version.Parse(version),
            AssemblyName = assembly,
            ApiVersion = apiVersion,
            Authors = ImmutableCollectionsMarshal.AsImmutableArray(authors.Split(',')),
            Description = description,
            ProjectUrl = Uri.TryCreate(projectUrl, UriKind.Absolute, out var uri) ? uri : null,
            Dependencies = ImmutableCollectionsMarshal.AsImmutableArray(dependencies)
        };
    }

    // The table lists each file's name, length and stored length; the stored files follow it in the same order.
    private static Dictionary<string, Entry> ReadEntries(BinaryReader reader, byte[] data)
    {
        var count = reader.ReadInt32();
        var headers = new (string Name, int Length, int StoredLength)[count];

        for (var i = 0; i < count; i++)
            headers[i] = (reader.ReadString(), reader.ReadInt32(), reader.ReadInt32());

        var entries = new Dictionary<string, Entry>(count, StringComparer.Ordinal);
        var offset = (int)reader.BaseStream.Position;

        foreach (var (name, length, storedLength) in headers)
        {
            if (offset + storedLength > data.Length)
                throw new InvalidDataException($"The archive's entry '{name}' is truncated.");

            entries[name] = new Entry(length, data, offset, storedLength);
            offset += storedLength;
        }

        return entries;
    }

    private readonly record struct Entry(int Length, byte[] Archive, int Offset, int StoredLength);
}
