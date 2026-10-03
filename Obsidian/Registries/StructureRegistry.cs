using Obsidian.WorldData.Structures;
using Obsidian.Nbt;
using System.IO;
using System.IO.Compression;

namespace Obsidian.Registries;

/// <summary>
/// Vanilla's structure templates by id, read from the directory <see cref="VanillaServerJar"/> extracted them to:
/// <c>minecraft:fossil/spine_1</c> is <c>fossil/spine_1.nbt</c> there.
/// </summary>
/// <remarks>
/// Templates aren't shipped with Obsidian, since Mojang's EULA doesn't allow redistributing them. They load on first use,
/// since most worlds only ever need part of them.
/// </remarks>
internal static class StructureRegistry
{
    private const string FileSuffix = ".nbt";

    // Lazy, so chunks generating in parallel that need the same template load it once.
    private static readonly ConcurrentDictionary<string, Lazy<StructureTemplate>> templates = new();

    private static string? directory;

    /// <summary>
    /// Sets the directory templates are read from, before any template is used.
    /// </summary>
    public static void Initialize(string structureDirectory) => directory = structureDirectory;

    /// <summary>Gets a template by id, loading it on first use; unknown ids get an empty template.</summary>
    public static StructureTemplate Get(string id) => templates.GetOrAdd(id, key => new Lazy<StructureTemplate>(() => Load(key))).Value;

    private static StructureTemplate Load(string id)
    {
        var root = directory ?? throw new InvalidOperationException("Structure templates are used before StructureRegistry.Initialize.");
        var path = id.StartsWith("minecraft:", StringComparison.Ordinal) ? id["minecraft:".Length..] : id;
        var file = Path.Combine(root, path + FileSuffix);

        // Like vanilla's StructureTemplateManager.getOrCreate, an unknown id is an empty template (one vanilla pool
        // references a template that doesn't exist).
        if (!File.Exists(file))
            return StructureTemplate.CreateEmpty();

        // Decompressed whole first: the reader reads a few bytes at a time, and each read of a gzip stream calls into zlib.
        using var decompressed = new MemoryStream();
        using (var gzip = new GZipStream(File.OpenRead(file), CompressionMode.Decompress))
            gzip.CopyTo(decompressed);

        decompressed.Position = 0;
        return StructureTemplate.Load(decompressed, NbtCompression.None);
    }
}
