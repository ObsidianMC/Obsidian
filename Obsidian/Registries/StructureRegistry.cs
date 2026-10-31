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

    // The current version's templates, for the 26.3 generator (see GetCurrent).
    private static readonly ConcurrentDictionary<string, Lazy<StructureTemplate>> currentTemplates = new();

    // Extracts the current version's templates and gives their directory; see InitializeCurrent.
    private static Func<Task<string>>? extractCurrent;

    // The extraction every template load waits on, replaced once it fails so that the next load tries again.
    private static Lazy<Task<string>>? currentExtraction;

    // See Use: the templates a structure on this thread starts and places with, instead of the legacy ones.
    [ThreadStatic]
    private static Func<string, StructureTemplate>? scoped;

    private static FrozenDictionary<string, StructureTemplate> overrides =
        FrozenDictionary<string, StructureTemplate>.Empty;

    /// <summary>
    /// Sets the directory templates are read from, before any template is used.
    /// </summary>
    public static void Initialize(string structureDirectory) => directory = structureDirectory;

    /// <summary>
    /// Sets how the current version's templates are extracted (see <see cref="GetCurrent"/>): only when the first
    /// one is needed, since the live generator places the legacy ones.
    /// </summary>
    /// <param name="extract">Extracts the templates and gives their directory, like
    /// <see cref="VanillaServerJar.ExtractStructuresAsync"/>.</param>
    public static void InitializeCurrent(Func<Task<string>> extract)
    {
        extractCurrent = extract;
        Volatile.Write(ref currentExtraction, new Lazy<Task<string>>(extract));
    }

    /// <summary>
    /// Gets one of the current version's vanilla templates by id, loaded on first use; unknown ids get an empty
    /// template. The 26.3 generator's structures and features read these, beside the legacy ones of <see cref="Get"/>.
    /// </summary>
    /// <remarks>
    /// The first load waits for the templates to be extracted. A failed extraction fails the loads waiting on it,
    /// and isn't kept: the next load extracts again.
    /// </remarks>
    public static StructureTemplate GetCurrent(string id)
    {
        var template = currentTemplates.GetOrAdd(
            Qualify(id),
            static key => new Lazy<StructureTemplate>(
                () => Load(key, CurrentDirectory(), nameof(InitializeCurrent)),
                LazyThreadSafetyMode.PublicationOnly));

        return template.Value;
    }

    private static string CurrentDirectory()
    {
        var extraction = Volatile.Read(ref currentExtraction)
            ?? throw new InvalidOperationException(
                "Structure templates are used before StructureRegistry.InitializeCurrent.");

        try
        {
            return extraction.Value.GetAwaiter().GetResult();
        }
        catch
        {
            Interlocked.CompareExchange(
                ref currentExtraction,
                new Lazy<Task<string>>(extractCurrent!),
                extraction);
            throw;
        }
    }

    /// <summary>
    /// Makes <see cref="Get"/> return <paramref name="templates"/>' templates on this thread until the scope is
    /// disposed, so that structures read from another version's data place that version's templates.
    /// </summary>
    /// <param name="templates">The templates by id, or <c>null</c> to keep the current ones.</param>
    public static TemplateScope Use(Func<string, StructureTemplate>? templates)
    {
        var previous = scoped;
        if (templates is not null)
            scoped = templates;

        return new TemplateScope(previous);
    }

    /// <summary>
    /// Restores the templates <see cref="Get"/> returned before <see cref="Use"/>.
    /// </summary>
    public readonly struct TemplateScope(Func<string, StructureTemplate>? previous) : IDisposable
    {
        public void Dispose() => scoped = previous;
    }

    /// <summary>
    /// Sets the templates of plugins' and data packs' files, by id, which replace vanilla's of the same id.
    /// </summary>
    public static void SetOverrides(FrozenDictionary<string, StructureTemplate> files)
    {
        Volatile.Write(ref overrides, files);
        Interlocked.Increment(ref overridesVersion);
    }

    private static long overridesVersion;

    /// <summary>
    /// Changes once the templates of plugins' and data packs' files change, after they did, for caches of what
    /// templates give (see <see cref="WorldData.Structures.Pools.StructureTemplatePool.MaxSize"/>). Unlike the
    /// templates, it doesn't keep them: vanilla's pools, which live as long as the server, cache against it.
    /// </summary>
    public static long OverridesVersion => Interlocked.Read(ref overridesVersion);

    /// <summary>
    /// Gets a template by id: a plugin's or data pack's, or vanilla's, loaded on first use. Unknown ids get an empty
    /// template, like vanilla's <c>StructureTemplateManager.getOrCreate</c>.
    /// </summary>
    public static StructureTemplate Get(string id)
    {
        id = Qualify(id);

        if (scoped is not null)
            return scoped(id);

        var files = Volatile.Read(ref overrides);
        if (files.Count > 0 && files.TryGetValue(id, out var file))
            return file;

        return templates.GetOrAdd(id, static key => new Lazy<StructureTemplate>(() =>
            Load(key, directory, nameof(Initialize)))).Value;
    }

    // Like vanilla's identifiers, one without a namespace is vanilla's.
    private static string Qualify(string id) => id.Contains(':') ? id : Vanilla + id;

    private static StructureTemplate Load(string id, string? directory, string initializer)
    {
        // Only vanilla's templates are in the directory; another namespace's is a file's or doesn't exist.
        if (!id.StartsWith(Vanilla, StringComparison.Ordinal))
            return StructureTemplate.CreateEmpty();

        var root = directory
            ?? throw new InvalidOperationException(
                $"Structure templates are used before StructureRegistry.{initializer}.");
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
