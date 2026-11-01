using Microsoft.Extensions.Logging;
using Obsidian.Nbt;
using Obsidian.Plugins;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Pools;
using System.Collections.Frozen;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Adds plugins' and data packs' structures, structure sets, template pools and structure templates from their data
/// files, like a data pack's.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>data/&lt;namespace&gt;/worldgen/structure/&lt;path&gt;.json</c>, <c>worldgen/structure_set/...</c> and
/// <c>worldgen/template_pool/...</c> are the entries <c>&lt;namespace&gt;:&lt;path&gt;</c>, read with
/// <see cref="WorldgenReader"/> into the same objects as vanilla's compiled ones. Processor lists are read with
/// features (see <see cref="PluginFeatures.ProcessorList"/>), as fossils name them too.</item>
/// <item><c>data/&lt;namespace&gt;/structure/&lt;path&gt;.nbt</c> is the template
/// <c>&lt;namespace&gt;:&lt;path&gt;</c>, vanilla's gzipped NBT.</item>
/// </list>
/// A file for one of vanilla's entries overrides it, and vanilla's entries naming it, or naming an overridden
/// placed feature or processor list, are read again from vanilla's files (see <see cref="VanillaOverrides"/>); when
/// files define the same entry, the last in order wins. Like features, structures aren't sent to clients, so they
/// come and go with their plugin: each <see cref="Apply"/> reads the loaded plugins' and data packs' files into a new
/// <see cref="WorldgenStructures"/>. Starts are saved by their structure's identifier, and kept as saved when it's
/// gone.
/// </remarks>
internal static partial class PluginStructures
{
    public const string Structure = "worldgen/structure";

    public const string StructureSet = "worldgen/structure_set";

    public const string TemplatePool = "worldgen/template_pool";

    /// <summary>
    /// The folders of plugins' structure, structure set and template pool files.
    /// </summary>
    public static FrozenSet<string> Registries { get; } = FrozenSet.Create(Structure, StructureSet, TemplatePool);

    private const string TemplateFolder = "structure/";

    private const string TemplateSuffix = ".nbt";

    private static readonly Lock applying = new();

    private static WorldgenStructures? current;

    // Each structure set built has a later version than the ones before, so caches keep the latest.
    private static long versions;

    // Gson reads data packs leniently, so comments and trailing commas are fine.
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// The structures chunk generation places: vanilla's with the loaded plugins' and data packs' files applied.
    /// </summary>
    public static WorldgenStructures Current => Volatile.Read(ref current) ?? WorldgenStructures.Vanilla;

    /// <summary>
    /// Finds the structure, structure set and template pool files in a plugin's files, by their paths.
    /// </summary>
    /// <returns>Each file's path, registry (one of <see cref="Registries"/>) and entry identifier.</returns>
    public static IEnumerable<(string Path, string Registry, string Identifier)> FindFiles(IEnumerable<string> paths) =>
        PluginFeatures.FindFiles(paths, Registries);

    /// <summary>
    /// Finds the structure templates in a plugin's files, by their paths:
    /// <c>data/&lt;namespace&gt;/structure/&lt;path&gt;.nbt</c> is the template <c>&lt;namespace&gt;:&lt;path&gt;</c>.
    /// </summary>
    public static IEnumerable<(string Path, string Identifier)> FindTemplates(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var parts = path.Split('/', 3);
            if (parts.Length < 3
                || parts[0] != "data"
                || !parts[2].StartsWith(TemplateFolder, StringComparison.Ordinal)
                || parts[2].Length <= TemplateFolder.Length + TemplateSuffix.Length
                || !path.EndsWith(TemplateSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            yield return (path, $"{parts[1]}:{parts[2][TemplateFolder.Length..^TemplateSuffix.Length]}");
        }
    }

    /// <summary>
    /// Reads a structure, structure set or template pool file. Entries are only checked to be JSON objects here:
    /// they're read when applied, since they may refer to other plugins' entries.
    /// </summary>
    /// <param name="registry">One of <see cref="Registries"/>.</param>
    /// <param name="identifier">The entry's identifier, like <c>ruby:ruins</c>.</param>
    /// <param name="source">Where the file comes from, for messages.</param>
    /// <exception cref="InvalidDataException">The file isn't valid.</exception>
    public static StructureFile Parse(string registry, string identifier, string source, ReadOnlySpan<byte> json)
    {
        if (!Registries.Contains(registry))
            throw new ArgumentException($"{registry} isn't a structure registry.", nameof(registry));

        if (!PluginTags.IsFileIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), documentOptions);
            return new StructureFile(registry, identifier, source, CodecJson.Object(document.RootElement, "").Clone());
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"{source} isn't a valid {registry} file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads a structure template file, vanilla's gzipped NBT.
    /// </summary>
    /// <param name="identifier">The template's identifier, like <c>ruby:ruins/tower</c>.</param>
    /// <param name="source">Where the file comes from, for messages.</param>
    /// <param name="maxSize">The most bytes the NBT may have, decompressed, like a data pack file's (see
    /// <see cref="DataBudget"/>).</param>
    /// <returns>The template, and the size of its NBT, decompressed.</returns>
    /// <exception cref="InvalidDataException">The file isn't a valid template, is too large, or declares more than
    /// its data holds or a template may keep.</exception>
    public static (TemplateFile File, long Size) ParseTemplate(
        string identifier,
        string source,
        byte[] data,
        long maxSize)
    {
        if (!PluginTags.IsFileIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        try
        {
            using var decompressed = new MemoryStream();
            using (var gzip = new GZipStream(new MemoryStream(data, writable: false), CompressionMode.Decompress))
            {
                var buffer = new byte[81_920];
                int read;

                // A gzip stream can decompress to far more than its size, so it's bounded as it's read.
                while ((read = gzip.Read(buffer)) > 0)
                {
                    if (decompressed.Length + read > maxSize)
                        throw new InvalidDataException($"it's larger than {maxSize} bytes decompressed");

                    decompressed.Write(buffer, 0, read);
                }
            }

            decompressed.Position = 0;
            var template = StructureTemplate.Load(decompressed, NbtCompression.None);
            return (new TemplateFile(identifier, source, template), decompressed.Length);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The NBT reader fails in many ways on data that isn't a template; any failure is the file's.
            throw new InvalidDataException($"{source} isn't a valid structure template: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Makes the structures of the loaded plugins' and data packs' files the <see cref="Current"/> ones, and their
    /// templates the ones <see cref="StructureRegistry"/> gives.
    /// </summary>
    /// <param name="files">The loaded plugins' structure files in load order, then data packs'.</param>
    /// <param name="templates">The loaded plugins' template files in load order, then data packs'.</param>
    /// <param name="features">The features and processor lists structures may name.</param>
    /// <param name="types">The worldgen types the loaded plugins registered, which files can name.</param>
    public static WorldgenStructures Apply(
        IEnumerable<StructureFile> files,
        IEnumerable<TemplateFile> templates,
        WorldgenFeatureSet features,
        IEnumerable<WorldgenTypeRegistration> types,
        ILogger logger)
    {
        lock (applying)
        {
            var snapshot = new StructureInputs([.. files], [.. templates], [.. types], logger);
            var structures = Build(snapshot.Files, snapshot.Templates, features, snapshot.Types, logger);

            StructureRegistry.SetOverrides(structures.Templates);
            Volatile.Write(ref current, structures);
            Volatile.Write(ref inputs, snapshot);
            return structures;
        }
    }

    /// <summary>
    /// Reads plugins' files into a structure set, without making it the current one. Files that can't be read are
    /// logged and left out, with the entries that need them.
    /// </summary>
    public static WorldgenStructures Build(
        IEnumerable<StructureFile> files,
        IEnumerable<TemplateFile> templates,
        WorldgenFeatureSet features,
        IEnumerable<WorldgenTypeRegistration> types,
        ILogger logger,
        WorldgenStructures? vanilla = null,
        Func<JsonElement, JsonElement>? normalize = null)
    {
        var kept = VanillaOverrides.LastWins(
            files,
            file => (file.Registry, file.Identifier),
            file => file.Source,
            logger);

        if (vanilla is null)
            kept.AddRange(VanillaDependents(kept, features));

        var keptTemplates = VanillaOverrides.LastWins(
            templates,
            file => (TemplateFolder, file.Identifier),
            file => file.Source,
            logger);

        var resolver = new StructureResolver(kept, WorldgenTypes.With(types), features, vanilla, normalize);

        foreach (var file in kept)
        {
            try
            {
                resolver.Read(file.Registry, file.Identifier);
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file.Source, ex.Message);
            }
        }

        var structures = new Dictionary<string, WorldData.Structures.Structure>(
            vanilla?.Structures ?? global::Obsidian.Registries.Structures.All);

        foreach (var (identifier, structure) in resolver.Structures)
            structures[identifier] = structure;

        var sets = new Dictionary<string, WorldData.Structures.StructureSet>(vanilla?.Sets ?? StructureSets.All);

        foreach (var (identifier, set) in resolver.Sets)
            sets[identifier] = set;

        var pools = new Dictionary<string, StructureTemplatePool>(
            vanilla?.Pools ?? FrozenDictionary<string, StructureTemplatePool>.Empty);
        foreach (var (identifier, pool) in resolver.Pools)
            pools[identifier] = pool;

        return new WorldgenStructures
        {
            Version = Interlocked.Increment(ref versions),
            Structures = structures.ToFrozenDictionary(),
            Sets = sets.ToFrozenDictionary(),
            Pools = pools.ToFrozenDictionary(),
            Templates = keptTemplates.ToFrozenDictionary(file => file.Identifier, file => file.Template),
            Owners = Owners(structures.Values, resolver, vanilla),
            LegacyPools = vanilla?.LegacyPools ?? true
        };
    }

    // The plugins each structure calls into while it starts and is placed: those whose types it's made of, through
    // the entries it names too. A jigsaw structure's pieces name pools by identifier, so any of them may reach any
    // pool: jigsaw structures, vanilla's compiled ones too, call into every plugin a pool read from files needs.
    private static FrozenDictionary<WorldData.Structures.Structure, ImmutableArray<PluginContainer>> Owners(
        IEnumerable<WorldData.Structures.Structure> structures,
        StructureResolver resolver,
        WorldgenStructures? vanilla = null)
    {
        var pools = resolver.Pools.Values
            .SelectMany(pool => resolver.Owners.GetValueOrDefault(pool, []))
            .ToHashSet();

        var owners = new Dictionary<WorldData.Structures.Structure, ImmutableArray<PluginContainer>>(
            ReferenceEqualityComparer.Instance);

        foreach (var structure in structures)
        {
            var previous = vanilla?.Owners.GetValueOrDefault(structure, []) ?? [];
            var plugins = resolver.Owners.GetValueOrDefault(structure, previous).ToHashSet();
            if (structure is JigsawStructure)
                plugins.UnionWith(pools);

            if (plugins.Count > 0)
                owners[structure] = [.. plugins];
        }

        return owners.ToFrozenDictionary(ReferenceEqualityComparer.Instance);
    }

    // Vanilla's entries naming an overridden one, read again from vanilla's files so that they use the override.
    // Features and processor lists are overridden with features (see PluginFeatures), so those read from files
    // count too. Templates are looked up by identifier as they're used, so naming one doesn't count.
    private static IEnumerable<StructureFile> VanillaDependents(List<StructureFile> files, WorldgenFeatureSet features)
    {
        var overridden = files
            .Select(file => (file.Registry, file.Identifier))
            .Concat(features.PlacedFeatures.Keys.Select(id => (PluginFeatures.PlacedFeature, id)))
            .Concat(features.ProcessorLists.Keys.Select(id => (PluginFeatures.ProcessorList, id)))
            .Where(entry => entry.Item2.StartsWith("minecraft:", StringComparison.Ordinal))
            .ToHashSet();

        if (overridden.Count == 0)
            return [];

        return VanillaOverrides.Dependents([Structure, StructureSet, TemplatePool], overridden)
            .Select(entry => new StructureFile(
                entry.Registry,
                entry.Identifier,
                $"vanilla's {entry.Identifier}",
                entry.Json));
    }

    /// <summary>
    /// Reads plugins' structure entries on demand, so that they can refer to each other in any order: each is read
    /// once and shared, and one that can't be read, or refers back to itself, fails every entry using it.
    /// </summary>
    private sealed class StructureResolver
    {
        private readonly Dictionary<(string Registry, string Identifier), StructureFile> files;
        private readonly Dictionary<StructureFile, string> failed = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<StructureFile> reading = new(ReferenceEqualityComparer.Instance);
        private readonly WorldgenFeatureSet features;
        private readonly WorldgenReader reader;
        private readonly WorldgenStructures vanilla;
        private readonly Func<JsonElement, JsonElement>? normalize;

        // The plugins whose types each entry is made of.
        private readonly WorldgenDependencies<PluginContainer> owners = new();

        public StructureResolver(
            List<StructureFile> files,
            WorldgenTypes types,
            WorldgenFeatureSet features,
            WorldgenStructures? vanilla = null,
            Func<JsonElement, JsonElement>? normalize = null)
        {
            this.normalize = normalize;
            this.vanilla = vanilla ?? WorldgenStructures.Vanilla;
            this.files = files.ToDictionary(file => (file.Registry, file.Identifier));
            this.features = features;

            var lookups = new StructureLookups(this.ProcessorListOf, this.PoolOf, this.StructureOf, this.SetOf);
            this.reader = new WorldgenReader(
                types,
                this.Configured,
                this.Placed,
                this.OnCreated,
                structures: lookups,
                inlineConfiguredFeature: features.InlineConfiguredLookup is null ? null
                    : json => this.FromFeatures(features.InlineConfiguredLookup(json), "inline", "feature"),
                inlinePlacedFeature: features.InlinePlacedLookup is null ? null
                    : json => this.FromFeatures(features.InlinePlacedLookup(json), "inline", "placed feature"));
        }

        public Dictionary<string, WorldData.Structures.Structure> Structures { get; } = [];

        public Dictionary<string, WorldData.Structures.StructureSet> Sets { get; } = [];

        public Dictionary<string, StructureTemplatePool> Pools { get; } = [];

        /// <summary>
        /// The plugins whose code each entry read runs, for the entries that run any.
        /// </summary>
        public Dictionary<object, ImmutableArray<PluginContainer>> Owners => this.owners.Of;

        /// <summary>
        /// Reads the entry of a file, if it wasn't read already.
        /// </summary>
        /// <exception cref="InvalidDataException">The entry, or one it needs, can't be read.</exception>
        public void Read(string registry, string identifier)
        {
            switch (registry)
            {
                case Structure:
                    this.StructureOf(identifier);
                    break;
                case StructureSet:
                    this.SetOf(identifier);
                    break;
                case TemplatePool:
                    this.PoolOf(identifier);
                    break;
            }
        }

        private WorldData.Structures.Structure StructureOf(string identifier) => this.Find(
            Structure,
            identifier,
            this.Structures,
            this.vanilla.Structures,
            file => this.reader.ReadStructure(file.Json, file.Identifier));

        private WorldData.Structures.StructureSet SetOf(string identifier) => this.Find(
            StructureSet,
            identifier,
            this.Sets,
            this.vanilla.Sets,
            file => this.reader.ReadStructureSet(file.Json, file.Identifier));

        private StructureTemplatePool PoolOf(string identifier) => this.Find(
            TemplatePool,
            identifier,
            this.Pools,
            this.vanilla.LegacyPools ? TemplatePools.All : this.vanilla.Pools,
            this.ReadPool);

        // Fallbacks are looked up by identifier as structures generate, since pools may fall back to each other, so
        // they're only checked to exist.
        private StructureTemplatePool ReadPool(StructureFile file)
        {
            var pool = this.reader.ReadTemplatePool(file.Json, file.Identifier);
            if (!this.files.ContainsKey((TemplatePool, pool.Fallback)) && this.vanilla.Pool(pool.Fallback) is null)
                throw new InvalidDataException($"fallback: {pool.Fallback} isn't a template pool");

            return pool;
        }

        // Plugins' features and processor lists, with vanilla's overridden ones, come from the feature set, and
        // bring the plugins they call into.
        private ConfiguredFeatureBase Configured(string identifier) => this.FromFeatures(
            this.features.FindConfigured(identifier),
            identifier,
            "configured feature");

        private PlacedFeature Placed(string identifier) => this.FromFeatures(
            this.features.FindPlaced(identifier),
            identifier,
            "placed feature");

        private StructureProcessorList ProcessorListOf(string identifier) => this.FromFeatures(
            this.features.ProcessorLookup is not null
                ? this.features.ProcessorLookup(identifier)
                : this.features.ProcessorLists.GetValueOrDefault(identifier)
                    ?? ProcessorLists.All.GetValueOrDefault(identifier),
            identifier,
            "processor list");

        private T FromFeatures<T>(T? entry, string identifier, string kind)
            where T : class
        {
            if (entry is null)
                throw new InvalidDataException($"{identifier} isn't a {kind}");

            foreach (var owner in this.features.OwnersOf(entry))
                this.owners.Add(owner);

            return entry;
        }

        // An entry by identifier: a file's, read once, or else vanilla's.
        private T Find<T>(
            string registry,
            string identifier,
            Dictionary<string, T> read,
            IReadOnlyDictionary<string, T> vanilla,
            Func<StructureFile, T> readFile)
            where T : class
        {
            if (!this.files.TryGetValue((registry, identifier), out var file))
            {
                return vanilla.TryGetValue(identifier, out var entry)
                    ? entry
                    : throw new InvalidDataException($"{identifier} isn't a {registry[(registry.IndexOf('/') + 1)..]}");
            }

            if (read.TryGetValue(identifier, out var done))
                return this.owners.Used(done);

            if (this.failed.ContainsKey(file))
                throw new InvalidDataException($"{identifier} isn't valid");

            if (!this.reading.Add(file))
                throw new InvalidDataException($"{identifier} refers back to itself");

            try
            {
                var entry = this.owners.Track(() =>
                {
                    var normalized = this.normalize is null ? file : file with { Json = this.normalize(file.Json) };
                    return readFile(normalized);
                });
                read[identifier] = entry;
                return this.owners.Used(entry);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.failed[file] = ex.Message;
                if (ex is InvalidDataException)
                    throw;

                throw new InvalidDataException(ex.Message, ex);
            }
            finally
            {
                this.reading.Remove(file);
            }
        }

        // An object the reader created, which belongs to a plugin when its type is from the plugin's assembly.
        private void OnCreated(Type type)
        {
            if (WorldgenDependencies<PluginContainer>.Owner(type) is PluginContainer owner)
                this.owners.Add(owner);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Left out {Source}: {Message}")]
        public static partial void Invalid(ILogger logger, string source, string message);
    }
}

/// <summary>
/// A plugin's or data pack's structure, structure set or template pool file.
/// </summary>
/// <param name="Registry">One of <see cref="PluginStructures.Registries"/>.</param>
/// <param name="Identifier">The entry's identifier, like <c>ruby:ruins</c>.</param>
/// <param name="Source">Where the file comes from, for messages.</param>
/// <param name="Json">The file's JSON.</param>
internal sealed record StructureFile(string Registry, string Identifier, string Source, JsonElement Json);

/// <summary>
/// A plugin's or data pack's structure template, from <c>data/&lt;namespace&gt;/structure/&lt;path&gt;.nbt</c>.
/// </summary>
/// <param name="Identifier">The template's identifier, which pool elements name in their <c>location</c>.</param>
/// <param name="Source">Where the file comes from, for messages.</param>
/// <param name="Template">The template read.</param>
internal sealed record TemplateFile(string Identifier, string Source, StructureTemplate Template);

/// <summary>
/// The structures chunk generation starts and places: vanilla's with plugins' and data packs' files applied.
/// Structure starts are cached by the structure sets they were made for, so each set built is a new
/// <see cref="Version"/>.
/// </summary>
internal sealed record WorldgenStructures
{
    /// <summary>
    /// Vanilla's structures alone.
    /// </summary>
    public static WorldgenStructures Vanilla { get; } = new()
    {
        Version = 0,
        Structures = global::Obsidian.Registries.Structures.All,
        Sets = StructureSets.All,
        Pools = FrozenDictionary<string, StructureTemplatePool>.Empty,
        Templates = FrozenDictionary<string, StructureTemplate>.Empty,
        Owners = FrozenDictionary<WorldData.Structures.Structure, ImmutableArray<PluginContainer>>.Empty
    };

    /// <summary>
    /// Later for every set built.
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// Every structure, by identifier.
    /// </summary>
    public required IReadOnlyDictionary<string, WorldData.Structures.Structure> Structures { get; init; }

    /// <summary>
    /// Every structure set, by identifier.
    /// </summary>
    public required IReadOnlyDictionary<string, WorldData.Structures.StructureSet> Sets { get; init; }

    /// <summary>
    /// The template pools of files, by identifier; vanilla's others are in <c>TemplatePools</c>.
    /// </summary>
    public required FrozenDictionary<string, StructureTemplatePool> Pools { get; init; }

    /// <summary>
    /// The templates of files, by identifier.
    /// </summary>
    public required FrozenDictionary<string, StructureTemplate> Templates { get; init; }

    /// <summary>
    /// The plugins whose code each structure runs while it starts and is placed, for those that run any. Starting and
    /// placing them is a call into those plugins (see <see cref="PluginCall"/>).
    /// </summary>
    public required FrozenDictionary<WorldData.Structures.Structure, ImmutableArray<PluginContainer>> Owners
    {
        get;
        init;
    }

    /// <summary>
    /// Whether <see cref="Pool"/> falls back to vanilla's compiled pools; the 26.3 structures hold all of theirs.
    /// </summary>
    public bool LegacyPools { get; init; } = true;

    /// <summary>
    /// A template pool by identifier, a file's or vanilla's, or <c>null</c>.
    /// </summary>
    public StructureTemplatePool? Pool(string identifier)
    {
        var pool = this.Pools.GetValueOrDefault(identifier);
        if (pool is not null || !this.LegacyPools)
            return pool;

        return TemplatePools.All.GetValueOrDefault(identifier);
    }
}
