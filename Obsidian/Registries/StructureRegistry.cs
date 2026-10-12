using Obsidian.WorldData.Structures;
using Obsidian.Nbt;
using System.Collections.Frozen;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Structure templates by id: plugins' and data packs' (see <see cref="PluginStructures"/>), then vanilla's, read from
/// the directory <see cref="VanillaServerJar"/> extracted them to: <c>minecraft:fossil/spine_1</c> is
/// <c>fossil/spine_1.nbt</c> there.
/// </summary>
/// <remarks>
/// Vanilla's templates aren't shipped with Obsidian, since Mojang's EULA doesn't allow redistributing them. They load
/// on first use, since most worlds only ever need part of them.
/// </remarks>
internal static class StructureRegistry
{
    private const string FileSuffix = ".nbt";

    private const string Vanilla = "minecraft:";

    // Lazy, so chunks generating in parallel that need the same template load it once.
    private static readonly ConcurrentDictionary<string, Lazy<StructureTemplate>> templates = new();

    private static string? directory;

    private static FrozenDictionary<string, StructureTemplate> overrides =
        FrozenDictionary<string, StructureTemplate>.Empty;

    /// <summary>
    /// Sets the directory templates are read from, before any template is used.
    /// </summary>
    public static void Initialize(string structureDirectory) => directory = structureDirectory;

    /// <summary>
    /// Sets the templates of plugins' and data packs' files, by id, which replace vanilla's of the same id.
    /// </summary>
    public static void SetOverrides(FrozenDictionary<string, StructureTemplate> files) =>
        Volatile.Write(ref overrides, files);

    /// <summary>
    /// The templates of plugins' and data packs' files now, a new object whenever they change, for caches of what
    /// templates give (see <see cref="WorldData.Structures.Pools.StructureTemplatePool.MaxSize"/>).
    /// </summary>
    public static object Overrides => Volatile.Read(ref overrides);

    /// <summary>
    /// Gets a template by id: a plugin's or data pack's, or vanilla's, loaded on first use. Unknown ids get an empty
    /// template, like vanilla's <c>StructureTemplateManager.getOrCreate</c>.
    /// </summary>
    public static StructureTemplate Get(string id)
    {
        // Like vanilla's identifiers, one without a namespace is vanilla's.
        if (!id.Contains(':'))
            id = Vanilla + id;

        var files = Volatile.Read(ref overrides);
        if (files.Count > 0 && files.TryGetValue(id, out var file))
            return file;

        return templates.GetOrAdd(id, static key => new Lazy<StructureTemplate>(() => Load(key))).Value;
    }

    private static StructureTemplate Load(string id)
    {
        // Only vanilla's templates are in the directory; another namespace's is a file's or doesn't exist.
        if (!id.StartsWith(Vanilla, StringComparison.Ordinal))
            return StructureTemplate.CreateEmpty();

        var root = directory ?? throw new InvalidOperationException("Structure templates are used before StructureRegistry.Initialize.");
        var path = id[Vanilla.Length..];
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
