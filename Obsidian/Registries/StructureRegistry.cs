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
/// on first use, since most worlds only ever need part of them. The server extracts the current version's at startup
/// (see <see cref="InitializeCurrent"/>); the legacy version's, which only the live generator places until Phase 5 of
/// <c>docs/porting-26.3-plan.md</c> switches it, are extracted once the first of them is needed.
/// </remarks>
internal static class StructureRegistry
{
    private const string FileSuffix = ".nbt";

    private const string Vanilla = "minecraft:";

    // Lazy, so chunks generating in parallel that need the same template load it once.
    private static readonly ConcurrentDictionary<string, Lazy<StructureTemplate>> templates = new();

    // The legacy version's templates, for the live generator (see Get).
    private static TemplateDirectory? directory;

    // The current version's templates, for the 26.3 generator (see GetCurrent).
    private static readonly ConcurrentDictionary<string, Lazy<StructureTemplate>> currentTemplates = new();

    private static TemplateDirectory? currentDirectory;

    // See Use: the templates a structure on this thread starts and places with, instead of the legacy ones.
    [ThreadStatic]
    private static Func<string, StructureTemplate>? scoped;

    private static FrozenDictionary<string, StructureTemplate> overrides =
        FrozenDictionary<string, StructureTemplate>.Empty;

    /// <summary>
    /// Sets the directory the legacy version's templates (see <see cref="Get"/>) are read from, before any template
    /// is used.
    /// </summary>
    public static void Initialize(string structureDirectory) =>
        Initialize(() => Task.FromResult(structureDirectory));

    /// <summary>
    /// Sets how the legacy version's templates (see <see cref="Get"/>) are extracted: only when the first one is
    /// needed, so a server whose worlds place none never downloads the legacy server jar.
    /// </summary>
    /// <param name="extract">Extracts the templates and gives their directory, like
    /// <see cref="VanillaServerJar.ExtractStructuresAsync"/>.</param>
    public static void Initialize(Func<Task<string>> extract) =>
        Volatile.Write(ref directory, new TemplateDirectory(extract));

    /// <summary>
    /// Sets the directory the current version's templates are read from, before any template is used.
    /// </summary>
    public static void InitializeCurrent(string structureDirectory) =>
        InitializeCurrent(() => Task.FromResult(structureDirectory));

    /// <summary>Sets how the current version's templates are extracted when first needed.</summary>
    /// <param name="extract">Extracts the templates and gives their directory, like
    /// <see cref="VanillaServerJar.ExtractStructuresAsync"/>.</param>
    public static void InitializeCurrent(Func<Task<string>> extract) =>
        Volatile.Write(ref currentDirectory, new TemplateDirectory(extract));

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
        id = Qualify(id);

        // Only vanilla's templates are extracted; another namespace's isn't cached, so the cache holds only vanilla's.
        if (!id.StartsWith(Vanilla, StringComparison.Ordinal))
            return StructureTemplate.CreateEmpty();

        return GetOrLoad(currentTemplates, id, static () => Volatile.Read(ref currentDirectory),
            nameof(InitializeCurrent));
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

        return GetOrLoad(templates, id, static () => Volatile.Read(ref directory), nameof(Initialize));
    }

    // Loads each template once, however many chunks ask for it at the same time. A load that fails, as when the
    // templates couldn't be extracted, isn't kept: the next call loads again.
    private static StructureTemplate GetOrLoad(
        ConcurrentDictionary<string, Lazy<StructureTemplate>> cache,
        string id,
        Func<TemplateDirectory?> directory,
        string initializer)
    {
        var template = cache.GetOrAdd(
            id,
            static (key, source) => new Lazy<StructureTemplate>(
                () => Load(key, source.Directory(), source.Initializer)),
            (Directory: directory, Initializer: initializer));

        try
        {
            return template.Value;
        }
        catch
        {
            cache.TryRemove(KeyValuePair.Create(id, template));
            throw;
        }
    }

    // Like vanilla's identifiers, one without a namespace is vanilla's.
    private static string Qualify(string id) => id.Contains(':') ? id : Vanilla + id;

    private static StructureTemplate Load(string id, TemplateDirectory? directory, string initializer)
    {
        // Only vanilla's templates are in the directory; another namespace's is a file's or doesn't exist.
        if (!id.StartsWith(Vanilla, StringComparison.Ordinal))
            return StructureTemplate.CreateEmpty();

        var root = (directory
            ?? throw new InvalidOperationException(
                $"Structure templates are used before StructureRegistry.{initializer}.")).Path;
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

    /// <summary>
    /// A version's extracted templates, extracted once the first of them is needed. Every load waits on the same
    /// extraction; one that fails isn't kept, so the next load extracts again.
    /// </summary>
    /// <remarks>
    /// Loads block on the extraction, so it runs on the thread pool, away from any synchronization context of the
    /// thread that first needed it. It doesn't take that thread's execution context either: the task lives as long as
    /// the server, and the context's async locals can hold a plugin, which then could never unload.
    /// </remarks>
    private sealed class TemplateDirectory(Func<Task<string>> extract)
    {
        private Lazy<Task<string>> extraction = new(() => Start(extract));

        /// <summary>The templates' directory, waiting for them to be extracted.</summary>
        public string Path
        {
            get
            {
                var current = Volatile.Read(ref this.extraction);

                try
                {
                    return current.Value.GetAwaiter().GetResult();
                }
                catch
                {
                    var retry = new Lazy<Task<string>>(() => Start(extract));
                    Interlocked.CompareExchange(ref this.extraction, retry, current);
                    throw;
                }
            }
        }

        private static Task<string> Start(Func<Task<string>> extract)
        {
            using (ExecutionContext.SuppressFlow())
                return Task.Run(extract);
        }
    }
}
