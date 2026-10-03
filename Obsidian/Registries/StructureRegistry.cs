using Obsidian.WorldData.Structures;
using Obsidian.Nbt;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Obsidian.Registries;

/// <summary>
/// Structure templates embedded under <c>Assets/Structures</c>, by id: <c>minecraft:fossil/spine_1</c> is
/// <c>Assets/Structures/fossil/spine_1.nbt</c>.
/// </summary>
/// <remarks>Templates load on first use, since most worlds only ever need part of them.</remarks>
internal static class StructureRegistry
{
    private const string ResourcePrefix = "Obsidian.Assets.Structures.";
    private const string ResourceSuffix = ".nbt";

    // Lazy, so chunks generating in parallel that need the same template load it once.
    private static readonly ConcurrentDictionary<string, Lazy<StructureTemplate>> templates = new();

    /// <summary>Gets a template by id, loading it on first use; unknown ids get an empty template.</summary>
    public static StructureTemplate Get(string id) => templates.GetOrAdd(id, key => new Lazy<StructureTemplate>(() => Load(key))).Value;

    private static StructureTemplate Load(string id)
    {
        var path = id.StartsWith("minecraft:", StringComparison.Ordinal) ? id["minecraft:".Length..] : id;
        var resource = ResourcePrefix + path.Replace('/', '.') + ResourceSuffix;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);

        // Like vanilla's StructureTemplateManager.getOrCreate, an unknown id is an empty template (one vanilla pool
        // references a template that doesn't exist).
        if (stream is null)
            return StructureTemplate.CreateEmpty();

        // Decompressed whole first: the reader reads a few bytes at a time, and each read of a gzip stream calls into zlib.
        using var decompressed = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
            gzip.CopyTo(decompressed);

        decompressed.Position = 0;
        return StructureTemplate.Load(decompressed, NbtCompression.None);
    }
}
