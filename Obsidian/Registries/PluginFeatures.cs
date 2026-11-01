using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Plugins;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Carvers;
using Obsidian.WorldData.Generators.Mojang.Features;
using Obsidian.WorldData.Structures;
using System.Collections.Frozen;
using System.IO;
using System.Text.Json;
using System.Threading;
using FeatureRegistry26 = Obsidian.WorldData.Generators.Features.FeatureRegistry;

namespace Obsidian.Registries;

/// <summary>
/// Adds plugins' and data packs' configured and placed features and configured carvers from their data files, and
/// applies plugins' biome modifiers to the biomes' features and carvers.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>data/&lt;namespace&gt;/worldgen/configured_feature/&lt;path&gt;.json</c> and
/// <c>worldgen/placed_feature/...</c> are the features <c>&lt;namespace&gt;:&lt;path&gt;</c>, read with
/// <see cref="WorldgenReader"/>. They refer to vanilla's or plugins' features by identifier, or define them
/// inline.</item>
/// <item><c>worldgen/configured_carver/...</c> are configured carvers, read with <see cref="ConfiguredCarvers"/>.
/// </item>
/// <item><c>data/&lt;namespace&gt;/biome_modifier/&lt;path&gt;.json</c> is a biome modifier (see
/// <see cref="BiomeModifierData"/>).</item>
/// </list>
/// A file for one of vanilla's features or carvers overrides it in the biomes that place it, and the vanilla features
/// naming it are read again from vanilla's files (see <see cref="VanillaOverrides"/>); when files define the same
/// entry, the last in order wins. A file for one of vanilla's biomes replaces its features and carvers. Unlike
/// biomes, features aren't sent to clients or saved, so they come and go with their plugin: each
/// <see cref="Apply"/> reads the loaded plugins' and data packs' files into a new <see cref="WorldgenFeatureSet"/> and
/// nothing keeps the previous one's features once generators moved to the new one.
/// </remarks>
internal static partial class PluginFeatures
{
    public const string ConfiguredFeature = "worldgen/configured_feature";

    public const string Feature = "worldgen/feature";

    public const string PlacedFeature = "worldgen/placed_feature";

    public const string ConfiguredCarver = "worldgen/configured_carver";

    public const string Carver = "worldgen/carver";

    public const string BiomeModifier = "biome_modifier";

    /// <summary>
    /// Structure processor lists, which features (fossils) and structures' template pools name.
    /// </summary>
    public const string ProcessorList = "worldgen/processor_list";

    /// <summary>
    /// The folders of plugins' feature, carver, processor list and biome modifier files.
    /// </summary>
    public static FrozenSet<string> Registries { get; } =
        FrozenSet.Create(ConfiguredFeature, Feature, PlacedFeature, ConfiguredCarver, Carver,
            BiomeModifier, ProcessorList);

    private static readonly Lock applying = new();

    private static WorldgenFeatureSet? current;

    // Gson reads data packs leniently, so comments and trailing commas are fine.
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// The features chunk generation uses: vanilla's with the loaded plugins' files applied.
    /// </summary>
    public static WorldgenFeatureSet Current => Volatile.Read(ref current) ?? WorldgenFeatureSet.Vanilla;

    /// <summary>
    /// Whether a placed feature is vanilla's or a loaded plugin's.
    /// </summary>
    public static bool IsPlacedFeature(string identifier) =>
        RegistryIds.IsPlacedFeature(identifier) || Current.PlacedFeatures.ContainsKey(identifier);

    /// <summary>
    /// Whether a configured carver is vanilla's or a loaded plugin's.
    /// </summary>
    public static bool IsConfiguredCarver(string identifier) =>
        RegistryIds.IsConfiguredCarver(identifier)
        || Current.ConfiguredCarvers.ContainsKey(identifier)
        // A 26.3 carver is intentionally absent from the legacy set. Later plugins' biome files can still name it;
        // schema validation and missing-dependency isolation belong to the native snapshot resolver.
        || Volatile.Read(ref inputs).Files.Any(file =>
            file.Registry == ConfiguredCarver && file.Identifier == identifier);

    /// <summary>
    /// Finds the feature and biome modifier files in a plugin's files, by their paths.
    /// </summary>
    /// <returns>Each file's path, registry (one of <see cref="Registries"/>) and entry identifier.</returns>
    public static IEnumerable<(string Path, string Registry, string Identifier)> FindFiles(IEnumerable<string> paths) =>
        FindFiles(paths, Registries);

    /// <summary>
    /// Finds the JSON files of some registries' folders in a plugin's files, by their paths:
    /// <c>data/&lt;namespace&gt;/&lt;registry&gt;/&lt;path&gt;.json</c> is the entry
    /// <c>&lt;namespace&gt;:&lt;path&gt;</c>.
    /// </summary>
    /// <returns>Each file's path, registry and entry identifier.</returns>
    public static IEnumerable<(string Path, string Registry, string Identifier)> FindFiles(
        IEnumerable<string> paths,
        IEnumerable<string> registries)
    {
        foreach (var path in paths)
        {
            var parts = path.Split('/', 3);
            if (parts.Length < 3 || parts[0] != "data" || !path.EndsWith(".json", StringComparison.Ordinal))
                continue;

            foreach (var registry in registries)
            {
                var folder = $"{registry}/";
                if (parts[2].Length > folder.Length + ".json".Length
                    && parts[2].StartsWith(folder, StringComparison.Ordinal))
                {
                    yield return (path, registry, $"{parts[1]}:{parts[2][folder.Length..^".json".Length]}");
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Reads a feature or biome modifier file. Features are only checked to be JSON objects here (or lists, for
    /// processor lists, which vanilla's codec reads either way): they're read when applied, since they may refer to
    /// other plugins' features.
    /// </summary>
    /// <param name="registry">One of <see cref="Registries"/>.</param>
    /// <param name="identifier">The entry's identifier, like <c>ruby:ore_ruby</c>.</param>
    /// <param name="source">Where the file comes from, for messages.</param>
    /// <exception cref="InvalidDataException">The file isn't valid.</exception>
    public static FeatureFile Parse(string registry, string identifier, string source, ReadOnlySpan<byte> json)
    {
        if (registry == Carver)
            registry = ConfiguredCarver;

        if (!PluginTags.IsFileIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), documentOptions);
            var root = registry == ProcessorList && document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.Clone()
                : CodecJson.Object(document.RootElement, "").Clone();

            var modifier = registry switch
            {
                BiomeModifier => BiomeModifierData.Read(root),
                ConfiguredFeature or Feature or PlacedFeature or ConfiguredCarver or ProcessorList => null,
                _ => throw new ArgumentException($"{registry} isn't read from plugins' files.", nameof(registry))
            };

            return new FeatureFile(registry, identifier, source, root, modifier);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"{source} isn't a valid {registry} file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Makes the features of the loaded plugins' files the <see cref="Current"/> ones.
    /// </summary>
    /// <param name="files">The loaded plugins' feature and biome modifier files in load order, then data
    /// packs'.</param>
    /// <param name="biomes">The loaded plugins' and data packs' codec files, in order, whose biomes' features are
    /// added.</param>
    /// <param name="types">The worldgen types the loaded plugins registered, which files can name.</param>
    /// <param name="generating">The identifiers of the dimensions levels generate now.</param>
    /// <returns>
    /// Whether the features are now the current ones. A level's generator can't move to features it can't order, so
    /// when a dimension in <paramref name="generating"/> can't be ordered, the previous features stay, and it's logged.
    /// </returns>
    public static bool Apply(
        IEnumerable<FeatureFile> files,
        IEnumerable<CodecFile> biomes,
        IEnumerable<WorldgenTypeRegistration> types,
        IReadOnlySet<string> generating,
        ILogger logger)
    {
        lock (applying)
        {
            var snapshot = new FeatureInputs([.. files], [.. biomes], [.. types], logger);
            var features = Build(snapshot.Files, snapshot.Biomes, snapshot.Types, logger);
            var broken = features.Unordered.FirstOrDefault(generating.Contains);

            if (broken is not null)
            {
                Log.KeptPrevious(logger, broken);
                return false;
            }

            Volatile.Write(ref current, features);
            Volatile.Write(ref inputs, snapshot);
            return true;
        }
    }

    /// <summary>
    /// Reads plugins' files into a feature set, without making it the current one. Files that can't be read are
    /// logged and left out, with the features and modifiers that need them.
    /// </summary>
    /// <param name="dimensions">
    /// The dimensions whose biomes' features must be ordered together; <see cref="PluginDimensions.All"/> by default.
    /// </param>
    public static WorldgenFeatureSet Build(
        IEnumerable<FeatureFile> files,
        IEnumerable<CodecFile> biomes,
        IEnumerable<WorldgenTypeRegistration> types,
        ILogger logger,
        IEnumerable<MojangDimension>? dimensions = null,
        FeatureRegistry26? registry = null)
    {
        var kept = VanillaOverrides.LastWins(
            files,
            file => (file.Registry, file.Identifier),
            file => file.Source,
            logger);

        if (registry is null)
            kept.AddRange(VanillaDependents(kept));

        var resolver = new FeatureResolver(kept, WorldgenTypes.With(types), registry);
        var carvers = ReadCarvers(registry is null ? kept : [], logger);

        foreach (var file in kept.Where(file =>
            file.Registry is ConfiguredFeature or Feature or PlacedFeature or ProcessorList))
        {
            try
            {
                if (file.Registry is ConfiguredFeature or Feature)
                    resolver.Configured(file.Identifier);
                else if (file.Registry == PlacedFeature)
                    resolver.Placed(file.Identifier);
                else
                    resolver.Processors(file.Identifier);
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file.Source, ex.Message);
            }
        }

        var biomeFeatures = new BiomeFeatureLists(registry?.VanillaBiomeFeatures()
            ?? global::Obsidian.Registries.BiomeFeatures.All, registry is null ? null : registry.SourceBiomes);
        var biomeCarvers = new BiomeCarverLists();

        // Vanilla's biomes place the overridden features and carvers, and the vanilla features read again.
        var vanillaPlaced = global::Obsidian.Registries.PlacedFeatures.All;
        if (registry is null)
            biomeFeatures.Replace(Overrides(resolver.PlacedFeatures, vanillaPlaced.ContainsKey, id => vanillaPlaced[id]));
        biomeCarvers.Replace(Overrides(carvers.Plugins, RegistryIds.IsConfiguredCarver, ConfiguredCarvers.Vanilla));

        foreach (var biome in PluginBiomes(biomes))
        {
            var element = (BiomeElement)biome.Element;

            try
            {
                biomeFeatures.Set(biome.Identifier, resolver.BiomeSteps(element));
            }
            catch (InvalidDataException ex)
            {
                Log.BiomeFeaturesLeftOut(logger, biome.Identifier, biome.Source, ex.Message);
            }

            if (registry is not null)
                continue;

            try
            {
                biomeCarvers.Set(biome.Identifier, carvers.Resolve(element.Carvers, "carvers"));
            }
            catch (InvalidDataException ex)
            {
                Log.BiomeCarversLeftOut(logger, biome.Identifier, biome.Source, ex.Message);
            }
        }

        // Every modifier's additions are applied before any modifier's removals, each in load order, so a removal
        // applies to what any plugin added. Everything a modifier names is resolved first, so one whose features or
        // carvers can't be resolved is left out whole. Spawns and spawn costs are PluginSpawns'.
        var modifiers = new List<ResolvedModifier>();

        foreach (var file in kept.Where(file => file.Modifier is not null))
        {
            if (registry is not null && !registry.Carvers.ValidModifiers.Contains(file))
                continue;

            try
            {
                var modifier = file.Modifier!;

                modifiers.Add(new ResolvedModifier(
                    ResolveBiomes(modifier),
                    resolver.StepFeatures(modifier.Add.Features, "add.features"),
                    registry is null ? carvers.Resolve(modifier.Add.Carvers, "add.carvers") : [],
                    resolver.StepFeatures(modifier.Remove.Features, "remove.features"),
                    registry is null ? carvers.Resolve(modifier.Remove.Carvers, "remove.carvers") : []));
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file.Source, ex.Message);
            }
        }

        var unmodified = biomeFeatures.Snapshot();

        foreach (var modifier in modifiers)
        {
            biomeFeatures.Add(modifier.Biomes, modifier.AddedFeatures);
            biomeCarvers.Add(modifier.Biomes, modifier.AddedCarvers);
        }

        foreach (var modifier in modifiers)
        {
            biomeFeatures.Remove(modifier.Biomes, modifier.RemovedFeatures);
            biomeCarvers.Remove(modifier.Biomes, modifier.RemovedCarvers);
        }

        var unordered = biomeFeatures.Changed
            ? biomeFeatures.Order([.. dimensions ?? PluginDimensions.All], unmodified, logger)
            : [];

        var result = biomeFeatures.Result;

        return new WorldgenFeatureSet(
            resolver.ConfiguredFeatures.ToFrozenDictionary(),
            resolver.PlacedFeatures.ToFrozenDictionary(),
            result,
            Owners(result, resolver.Owners))
        {
            Unordered = unordered,
            ConfiguredCarvers = carvers.Plugins.ToFrozenDictionary(),
            BiomeCarvers = biomeCarvers.Result,
            ProcessorLists = resolver.ProcessorLists.ToFrozenDictionary(),
            EntryOwners = resolver.Owners.ToFrozenDictionary(ReferenceEqualityComparer.Instance),
            ConfiguredLookup = registry is null ? null : registry.FindConfigured,
            PlacedLookup = registry is null ? null : registry.FindPlaced,
            ProcessorLookup = registry is null ? null : registry.Processors,
            OwnerLookup = registry is null ? null : registry.OwnersOf,
            InlineConfiguredLookup = registry is null ? null : registry.ConfiguredAdapter,
            InlinePlacedLookup = registry is null ? null : registry.PlacedAdapter
        };
    }

    /// <summary>
    /// The biomes a modifier changes: its <c>biomes</c> less those it <c>exclude</c>s.
    /// </summary>
    /// <exception cref="InvalidDataException">A biome or tag doesn't exist.</exception>
    public static List<string> ResolveBiomes(BiomeModifierData modifier)
    {
        var excluded = ResolveBiomes(modifier.Exclude, "exclude").ToHashSet();

        return [.. ResolveBiomes(modifier.Biomes, "biomes").Where(biome => !excluded.Contains(biome)).Distinct()];
    }

    /// <summary>
    /// A set of biomes to their identifiers: vanilla's or plugins' biomes, and biome tags, vanilla's or plugins'
    /// (see <see cref="PluginTags"/>).
    /// </summary>
    /// <exception cref="InvalidDataException">A biome or tag doesn't exist.</exception>
    public static IEnumerable<string> ResolveBiomes(ImmutableArray<string> biomes, string path)
    {
        foreach (var entry in biomes)
        {
            if (!entry.StartsWith('#'))
            {
                yield return CodecRegistry.TryGetBiome(entry, out _)
                    ? entry
                    : throw new InvalidDataException($"{path}: {entry} isn't a biome");

                continue;
            }

            if (!TagsRegistry.TryGet(PluginTags.Biome, entry[1..], out var tag))
                throw new InvalidDataException($"{path}: {entry} isn't a biome tag");

            foreach (var id in tag.Entries)
            {
                if (CodecRegistry.TryGetBiome(id, out var biome))
                    yield return biome!.Name;
            }
        }
    }

    // A tag's identifiers, for registries whose tags hold identifiers (placed features and configured carvers).
    private static ImmutableArray<string> TagEntries(string registry, string tag, string path) =>
        PluginTags.TryGetIdentifiers(registry, tag[1..], out var entries)
            ? entries
            : throw new InvalidDataException($"{path}: {tag} isn't a {registry} tag");

    // The plugins' configured carvers that can be read; the others are logged and left out.
    private static CarverResolver ReadCarvers(List<FeatureFile> files, ILogger logger)
    {
        var carvers = new Dictionary<string, IConfiguredCarver>();

        foreach (var file in files.Where(file => file.Registry == ConfiguredCarver))
        {
            try
            {
                carvers[file.Identifier] = ConfiguredCarvers.Read(file.Json);
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file.Source, ex.Message);
            }
        }

        return new CarverResolver(carvers);
    }

    // The plugins whose code the biomes' features run, for the features that run any.
    private static FrozenDictionary<PlacedFeature, ImmutableArray<PluginContainer>> Owners(
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> biomeFeatures,
        Dictionary<object, ImmutableArray<PluginContainer>> owners)
    {
        if (owners.Count == 0)
            return FrozenDictionary<PlacedFeature, ImmutableArray<PluginContainer>>.Empty;

        var result = new Dictionary<PlacedFeature, ImmutableArray<PluginContainer>>();

        foreach (var feature in biomeFeatures.Values.SelectMany(steps => steps).SelectMany(step => step))
        {
            if (owners.TryGetValue(feature, out var plugins))
                result.TryAdd(feature, plugins);
        }

        return result.ToFrozenDictionary();
    }

    // The biome files that apply, the last in order for each biome; PluginCodecs logs those replaced.
    private static IEnumerable<CodecFile> PluginBiomes(IEnumerable<CodecFile> files) =>
        PluginCodecs.Winners(files, NullLogger.Instance).Where(file => file.Registry == PluginCodecs.Biome);

    // Vanilla's features naming an overridden one, read again from vanilla's files so that they use the override.
    private static IEnumerable<FeatureFile> VanillaDependents(List<FeatureFile> files)
    {
        // Carvers and biome modifiers aren't named by features; processor lists are, by fossils.
        var overridden = files
            .Where(file => file.Registry is ConfiguredFeature or PlacedFeature or ProcessorList)
            .Select(file => (file.Registry, file.Identifier))
            .ToHashSet();

        if (!overridden.Any(entry => entry.Identifier.StartsWith("minecraft:", StringComparison.Ordinal)))
            return [];

        return VanillaOverrides.Dependents([ConfiguredFeature, PlacedFeature, ProcessorList], overridden)
            .Select(entry => new FeatureFile(
                entry.Registry,
                entry.Identifier,
                $"vanilla's {entry.Identifier}",
                entry.Json,
                null));
    }

    // Vanilla's compiled entries that files' entries of the same identifier replace, to what replaces them.
    private static Dictionary<T, T> Overrides<T>(
        IReadOnlyDictionary<string, T> read,
        Func<string, bool> isVanilla,
        Func<string, T> vanilla)
        where T : class
    {
        var replaced = new Dictionary<T, T>(ReferenceEqualityComparer.Instance);

        foreach (var (identifier, entry) in read)
        {
            if (isVanilla(identifier))
                replaced[vanilla(identifier)] = entry;
        }

        return replaced;
    }

    /// <summary>
    /// Reads plugins' features on demand, so that features can refer to each other in any order: each is read once
    /// and shared, and one that can't be read, or refers back to itself, fails every feature using it.
    /// </summary>
    private sealed class FeatureResolver
    {
        private readonly Dictionary<string, FeatureFile> configuredFiles;
        private readonly Dictionary<string, FeatureFile> placedFiles;
        private readonly Dictionary<string, FeatureFile> processorListFiles;
        private readonly Dictionary<FeatureFile, string> failed = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<FeatureFile> reading = new(ReferenceEqualityComparer.Instance);
        private readonly WorldgenReader reader;
        private readonly FeatureRegistry26? registry;

        // The plugins whose types each feature is made of.
        private readonly WorldgenDependencies<PluginContainer> owners = new();

        public FeatureResolver(List<FeatureFile> files, WorldgenTypes types, FeatureRegistry26? registry = null)
        {
            this.registry = registry;
            this.configuredFiles = files
                .Where(file => file.Registry == ConfiguredFeature)
                .ToDictionary(file => file.Identifier);

            this.placedFiles = files
                .Where(file => file.Registry == PlacedFeature)
                .ToDictionary(file => file.Identifier);

            this.processorListFiles = files
                .Where(file => file.Registry == ProcessorList)
                .ToDictionary(file => file.Identifier);

            this.reader = new WorldgenReader(
                types,
                this.Configured,
                this.Placed,
                this.OnCreated,
                structures: StructureLookups.Vanilla with { FindProcessorList = this.Processors });
        }

        public Dictionary<string, ConfiguredFeatureBase> ConfiguredFeatures { get; } = [];

        public Dictionary<string, PlacedFeature> PlacedFeatures { get; } = [];

        public Dictionary<string, StructureProcessorList> ProcessorLists { get; } = [];

        /// <summary>
        /// The plugins whose code each feature read runs: those whose types it, or a feature it refers to, is made
        /// of. Features made of Obsidian's types alone aren't in it.
        /// </summary>
        public Dictionary<object, ImmutableArray<PluginContainer>> Owners => this.owners.Of;

        public ConfiguredFeatureBase Configured(string identifier)
        {
            if (this.registry is not null)
                return this.FromRegistry(identifier, this.ConfiguredFeatures, this.registry.ConfiguredAdapter);

            if (!this.configuredFiles.ContainsKey(identifier)
                && global::Obsidian.Registries.ConfiguredFeatures.All.TryGetValue(identifier, out var vanilla))
            {
                return vanilla;
            }

            if (!this.ConfiguredFeatures.TryGetValue(identifier, out var feature))
            {
                if (!this.configuredFiles.TryGetValue(identifier, out var file))
                    throw new InvalidDataException($"{identifier} isn't a configured feature");

                feature = this.Read(
                    file,
                    this.ConfiguredFeatures,
                    () => this.reader.ReadConfiguredFeature(file.Json, file.Identifier));
            }

            return this.Used(feature);
        }

        public PlacedFeature Placed(string identifier)
        {
            if (this.registry is not null)
                return this.FromRegistry(identifier, this.PlacedFeatures, this.registry.PlacedAdapter);

            if (!this.placedFiles.ContainsKey(identifier)
                && global::Obsidian.Registries.PlacedFeatures.All.TryGetValue(identifier, out var vanilla))
            {
                return vanilla;
            }

            if (!this.PlacedFeatures.TryGetValue(identifier, out var feature))
            {
                if (!this.placedFiles.TryGetValue(identifier, out var file))
                    throw new InvalidDataException($"{identifier} isn't a placed feature");

                feature = this.Read(
                    file,
                    this.PlacedFeatures,
                    () => this.reader.ReadPlacedFeature(file.Json, file.Identifier));
            }

            return this.Used(feature);
        }

        public StructureProcessorList Processors(string identifier)
        {
            if (this.registry is not null)
                return this.FromRegistry(identifier, this.ProcessorLists, this.registry.Processors);

            if (!this.processorListFiles.ContainsKey(identifier)
                && global::Obsidian.Registries.ProcessorLists.All.TryGetValue(identifier, out var vanilla))
            {
                return vanilla;
            }

            if (!this.ProcessorLists.TryGetValue(identifier, out var list))
            {
                if (!this.processorListFiles.TryGetValue(identifier, out var file))
                    throw new InvalidDataException($"{identifier} isn't a processor list");

                list = this.Read(
                    file,
                    this.ProcessorLists,
                    () => this.reader.ReadProcessorList(file.Json, file.Identifier));
            }

            return this.Used(list);
        }

        /// <summary>
        /// The placed features a holder set entry given as JSON stands for: a <c>#tag</c>'s, or the one given.
        /// </summary>
        public IEnumerable<PlacedFeature> PlacedSet(JsonElement json, string path) =>
            json.ValueKind == JsonValueKind.String && json.GetString()!.StartsWith('#')
                ? TagEntries(PlacedFeature, json.GetString()!, path).Select(this.Placed).ToList()
                : [this.Placed(json, path)];

        /// <summary>
        /// A biome modifier's features of each step it names.
        /// </summary>
        public List<(DecorationStep Step, List<PlacedFeature> Features)> StepFeatures(
            ImmutableArray<ModifierFeatures> steps,
            string path)
        {
            var result = new List<(DecorationStep Step, List<PlacedFeature> Features)>();

            foreach (var step in steps)
            {
                var stepPath = $"{path}.{step.Step.ToString().ToSnakeCase()}";
                var features = step.Features
                    .SelectMany((feature, i) => this.PlacedSet(feature, $"{stepPath}[{i}]"))
                    .ToList();

                result.Add((step.Step, features));
            }

            return result;
        }

        /// <summary>
        /// A placed feature given as JSON: an identifier, or one defined inline, which is a new feature each time.
        /// </summary>
        public PlacedFeature Placed(JsonElement json, string path)
        {
            if (json.ValueKind == JsonValueKind.String)
                return this.Placed(json.GetString()!);

            if (this.registry is not null)
            {
                var placed = this.registry.PlacedAdapter(json);
                this.CopyOwners(placed);
                return placed;
            }

            return this.Used(this.owners.Track(() => this.reader.ReadPlacedFeature(json, null, path)));
        }

        private T FromRegistry<T>(string id, Dictionary<string, T> entries, Func<string, T> find) where T : class
        {
            var entry = find(id);
            entries[id] = entry;
            this.CopyOwners(entry);
            return entry;
        }

        private void CopyOwners(object entry)
        {
            if (this.registry!.EntryOwners.TryGetValue(entry, out var plugins))
                this.owners.Of[entry] = plugins;
        }

        /// <summary>
        /// The features of a plugin biome's steps, given as identifiers or inline features' JSON text.
        /// </summary>
        public List<List<PlacedFeature>> BiomeSteps(BiomeElement biome)
        {
            var steps = new List<List<PlacedFeature>>();

            for (var step = 0; step < biome.Features.Length; step++)
            {
                var features = new List<PlacedFeature>();

                foreach (var entry in biome.Features[step])
                {
                    var path = $"features[{step}]";

                    if (entry.StartsWith('#'))
                        features.AddRange(TagEntries(PlacedFeature, entry, path).Select(this.Placed));
                    else if (entry.StartsWith('{'))
                        features.Add(this.Placed(ParseInline(entry, path), path));
                    else
                        features.Add(this.Placed(entry));
                }

                steps.Add(features);
            }

            return steps;
        }

        // The biome file was read leniently, so a feature's or carver's JSON text can have comments and trailing
        // commas.
        public static JsonElement ParseInline(string json, string path)
        {
            try
            {
                using var document = JsonDocument.Parse(json, documentOptions);
                return document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw CodecJson.Error(path, ex.Message);
            }
        }

        private T Read<T>(FeatureFile file, Dictionary<string, T> features, Func<T> read)
            where T : class
        {
            if (this.failed.ContainsKey(file))
                throw new InvalidDataException($"{file.Identifier} isn't valid");

            if (!this.reading.Add(file))
                throw new InvalidDataException($"{file.Identifier} refers back to itself");

            try
            {
                var feature = this.owners.Track(read);
                features[file.Identifier] = feature;
                return feature;
            }
            catch (InvalidDataException ex)
            {
                this.failed[file] = ex.Message;
                throw;
            }
            finally
            {
                this.reading.Remove(file);
            }
        }

        // A feature used by the one being read, if any, which then runs its plugins' code too.
        private T Used<T>(T feature)
            where T : class =>
            this.owners.Used(feature);

        // An object the reader created, which belongs to a plugin when its type is from the plugin's assembly.
        private void OnCreated(Type type)
        {
            if (WorldgenDependencies<PluginContainer>.Owner(type) is PluginContainer owner)
                this.owners.Add(owner);
        }
    }

    /// <summary>
    /// The biomes' features per decoration step, starting from vanilla's <c>BiomeFeatures.All</c>, which stays
    /// the result as long as nothing changes it.
    /// </summary>
    private sealed class BiomeFeatureLists(
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> vanilla,
        Func<MojangDimension, IEnumerable<BiomeCodec>>? sourceBiomes = null)
    {
        private readonly Dictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> lists =
            new(vanilla);

        private bool changed;

        public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> Result =>
            this.changed ? this.lists.ToFrozenDictionary() : vanilla;

        /// <summary>
        /// Whether any biome's features differ from vanilla's.
        /// </summary>
        public bool Changed => this.changed;

        /// <summary>
        /// Gives a biome its features.
        /// </summary>
        // A biome file's features; one overriding a vanilla biome replaces its features even with none.
        public void Set(string biome, List<List<PlacedFeature>> steps)
        {
            if (steps.Count == 0 && !this.lists.ContainsKey(biome))
                return;

            this.lists[biome] = steps;
            this.changed = true;
        }

        // Swaps features in every biome's steps.
        public void Replace(Dictionary<PlacedFeature, PlacedFeature> replaced)
        {
            if (replaced.Count == 0)
                return;

            foreach (var (biome, steps) in this.lists.ToList())
            {
                if (!steps.Any(step => step.Any(replaced.ContainsKey)))
                    continue;

                this.lists[biome] = [.. steps.Select(step => step
                    .Select(feature => replaced.GetValueOrDefault(feature, feature))
                    .ToList())];
                this.changed = true;
            }
        }

        /// <summary>
        /// Adds features to the end of the biomes' steps. Their order isn't checked here: a later modifier may
        /// still change it.
        /// </summary>
        public void Add(List<string> biomes, List<(DecorationStep Step, List<PlacedFeature> Features)> added)
        {
            if (added.Count == 0)
                return;

            foreach (var biome in biomes)
            {
                var steps = this.Steps(biome);

                foreach (var (step, features) in added)
                {
                    while (steps.Count <= (int)step)
                        steps.Add([]);

                    steps[(int)step].AddRange(features);
                }

                this.lists[biome] = steps;
            }

            this.changed |= biomes.Count > 0;
        }

        /// <summary>
        /// Removes features from the biomes' steps.
        /// </summary>
        public void Remove(List<string> biomes, List<(DecorationStep Step, List<PlacedFeature> Features)> removed)
        {
            if (removed.Count == 0)
                return;

            foreach (var biome in biomes)
            {
                var steps = this.Steps(biome);

                foreach (var (step, features) in removed)
                {
                    var set = features.ToHashSet(ReferenceEqualityComparer.Instance);

                    if ((int)step < steps.Count)
                        steps[(int)step].RemoveAll(set.Contains);
                }

                this.lists[biome] = steps;
            }

            this.changed |= biomes.Count > 0;
        }

        // A copy of the biome's steps to change.
        private List<List<PlacedFeature>> Steps(string biome) =>
            this.lists.GetValueOrDefault(biome)?.Select(step => step.ToList()).ToList() ?? [];

        /// <summary>
        /// The biomes' features as they are now, to <see cref="Revert"/> to.
        /// </summary>
        public Dictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> Snapshot() => new(this.lists);

        /// <summary>
        /// Whether the dimension's generator can't order its biomes' features, like vanilla's FeatureSorter, which
        /// sorts the features of its biome source's possible biomes alone.
        /// </summary>
        public bool HasCycle(MojangDimension dimension)
        {
            try
            {
                var biomes = sourceBiomes?.Invoke(dimension) ?? dimension.PossibleBiomes;
                FeatureSorter.Build(biomes.ToArray(), biome => this.lists.GetValueOrDefault(biome.Name) ?? []);
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        /// <summary>
        /// Makes every dimension's biomes' features orderable where it can, by giving the biomes of each dimension
        /// that can't be ordered back their features from <paramref name="unmodified"/>.
        /// </summary>
        /// <returns>The dimensions that still can't be ordered: their biomes' own features are in a cycle.</returns>
        /// <remarks>
        /// Vanilla's generator stops when it can't order the features of its biome source's possible biomes, which
        /// happens when one is placed both before and after another in different biomes. NeoForge builds biomes once
        /// every phase ran, so only the final lists count. Dimensions share biomes, so reverting one dimension's
        /// biomes can break another checked before it: the checks repeat until nothing more is reverted. Reverting
        /// only ever goes back to the unmodified lists, so this ends.
        /// </remarks>
        public ImmutableArray<string> Order(
            List<MojangDimension> dimensions,
            Dictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> unmodified,
            ILogger logger)
        {
            bool reverted;

            do
            {
                reverted = false;

                foreach (var dimension in dimensions)
                {
                    if (this.HasCycle(dimension) && this.Revert(dimension, unmodified))
                    {
                        Log.ModifiersLeftOut(logger, dimension.Identifier);
                        reverted = true;
                    }
                }
            }
            while (reverted);

            var unordered = dimensions
                .Where(this.HasCycle)
                .Select(dimension => dimension.Identifier)
                .Distinct()
                .ToImmutableArray();

            foreach (var dimension in unordered)
                Log.Unordered(logger, dimension);

            return unordered;
        }

        // Gives the dimension's biomes back the features they had in the snapshot; whether any changed.
        private bool Revert(
            MojangDimension dimension,
            Dictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> snapshot)
        {
            var changed = false;

            foreach (var biome in sourceBiomes?.Invoke(dimension) ?? dimension.PossibleBiomes)
            {
                var current = this.lists.GetValueOrDefault(biome.Name);
                var steps = snapshot.GetValueOrDefault(biome.Name);

                if (ReferenceEquals(current, steps))
                    continue;

                if (steps is not null)
                    this.lists[biome.Name] = steps;
                else
                    this.lists.Remove(biome.Name);

                changed = true;
            }

            return changed;
        }
    }

    // A biome modifier's biomes, features and carvers, resolved before any modifier is applied.
    private sealed record ResolvedModifier(
        List<string> Biomes,
        List<(DecorationStep Step, List<PlacedFeature> Features)> AddedFeatures,
        List<IConfiguredCarver> AddedCarvers,
        List<(DecorationStep Step, List<PlacedFeature> Features)> RemovedFeatures,
        List<IConfiguredCarver> RemovedCarvers);

    /// <summary>
    /// Finds configured carvers: the plugins' that could be read, or vanilla's.
    /// </summary>
    private sealed class CarverResolver(Dictionary<string, IConfiguredCarver> plugins)
    {
        /// <summary>
        /// The plugins' configured carvers that could be read, by identifier.
        /// </summary>
        public Dictionary<string, IConfiguredCarver> Plugins => plugins;

        /// <summary>
        /// A set of configured carvers: identifiers, <c>#tag</c>s and inline carvers' JSON text, as biome files and
        /// biome modifiers give them. An inline carver is a new carver each time.
        /// </summary>
        /// <exception cref="InvalidDataException">A carver or tag doesn't exist, or an inline carver can't be
        /// read.</exception>
        public List<IConfiguredCarver> Resolve(ImmutableArray<string> carvers, string path)
        {
            return [.. carvers.SelectMany(entry => entry.StartsWith('#')
                ? TagEntries(ConfiguredCarver, entry, path).Select(identifier => this.Find(identifier, path))
                : [entry.StartsWith('{') ? Inline(entry, path) : this.Find(entry, path)])];
        }

        private static IConfiguredCarver Inline(string json, string path)
        {
            var carver = FeatureResolver.ParseInline(json, path);

            try
            {
                return ConfiguredCarvers.Read(carver);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"{path}: {ex.Message}", ex);
            }
        }

        private IConfiguredCarver Find(string identifier, string path)
        {
            if (plugins.TryGetValue(identifier, out var carver))
                return carver;

            return RegistryIds.IsConfiguredCarver(identifier)
                ? ConfiguredCarvers.Vanilla(identifier)
                : throw new InvalidDataException($"{path}: {identifier} isn't a configured carver");
        }
    }

    /// <summary>
    /// The biomes' configured carvers, starting from vanilla's biomes', which stay the result as long as nothing
    /// changes them.
    /// </summary>
    private sealed class BiomeCarverLists
    {
        private readonly Dictionary<string, ImmutableArray<IConfiguredCarver>> lists =
            new(ConfiguredCarvers.VanillaBiomes);

        private bool changed;

        public IReadOnlyDictionary<string, ImmutableArray<IConfiguredCarver>> Result =>
            this.changed ? this.lists.ToFrozenDictionary() : ConfiguredCarvers.VanillaBiomes;

        /// <summary>
        /// Gives a plugin's biome its carvers.
        /// </summary>
        public void Set(string biome, List<IConfiguredCarver> carvers)
        {
            this.lists[biome] = [.. carvers];
            this.changed = true;
        }

        // Swaps carvers in every biome's list.
        public void Replace(Dictionary<IConfiguredCarver, IConfiguredCarver> replaced)
        {
            if (replaced.Count == 0)
                return;

            foreach (var (biome, carvers) in this.lists.ToList())
            {
                if (!carvers.Any(replaced.ContainsKey))
                    continue;

                this.lists[biome] = [.. carvers.Select(carver => replaced.GetValueOrDefault(carver, carver))];
                this.changed = true;
            }
        }

        /// <summary>
        /// Adds carvers to the end of the biomes' carvers.
        /// </summary>
        public void Add(List<string> biomes, List<IConfiguredCarver> added)
        {
            if (added.Count == 0)
                return;

            foreach (var biome in biomes)
                this.lists[biome] = this.lists.GetValueOrDefault(biome, []).AddRange(added);

            this.changed |= biomes.Count > 0;
        }

        /// <summary>
        /// Removes carvers from the biomes' carvers.
        /// </summary>
        public void Remove(List<string> biomes, List<IConfiguredCarver> removed)
        {
            if (removed.Count == 0)
                return;

            var set = removed.ToHashSet(ReferenceEqualityComparer.Instance);

            foreach (var biome in biomes)
                this.lists[biome] = this.lists.GetValueOrDefault(biome, []).RemoveAll(set.Contains);

            this.changed |= biomes.Count > 0;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Left out {Source}: {Message}")]
        public static partial void Invalid(ILogger logger, string source, string message);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Left out the features of {Identifier} from {Source}: {Message}")]
        public static partial void BiomeFeaturesLeftOut(
            ILogger logger,
            string identifier,
            string source,
            string message);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Left out the carvers of {Identifier} from {Source}: {Message}")]
        public static partial void BiomeCarversLeftOut(
            ILogger logger,
            string identifier,
            string source,
            string message);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Left out plugins' biome modifiers in {Dimension}: with them, its biomes' features would be in "
                + "a cycle, one placed both before and after another")]
        public static partial void ModifiersLeftOut(ILogger logger, string dimension);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Can't generate {Dimension}: its biomes' own features would be in a cycle, one placed both "
                + "before and after another")]
        public static partial void Unordered(ILogger logger, string dimension);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Kept the previous features: with the loaded plugins' files, the features of {Dimension}, "
                + "which is generating, can't be ordered")]
        public static partial void KeptPrevious(ILogger logger, string dimension);
    }
}

/// <summary>
/// A plugin's configured feature, placed feature, configured carver or biome modifier file.
/// </summary>
/// <param name="Registry">One of <see cref="PluginFeatures.Registries"/>.</param>
/// <param name="Identifier">The entry's identifier, like <c>ruby:ore_ruby</c>.</param>
/// <param name="Source">Where the file comes from, for messages.</param>
/// <param name="Json">The file's JSON.</param>
/// <param name="Modifier">The biome modifier, for a biome modifier file.</param>
internal sealed record FeatureFile(
    string Registry,
    string Identifier,
    string Source,
    JsonElement Json,
    BiomeModifierData? Modifier)
{
    // File-only worldgen still belongs to its supplying plugin, which unloading must stop entering.
    internal PluginContainer? Owner { get; init; }
}

/// <summary>
/// The features and carvers chunk generation places: the loaded plugins' features, and every biome's features per
/// decoration step and carvers, which are vanilla's with plugins' biomes and biome modifiers applied.
/// </summary>
/// <param name="ConfiguredFeatures">The plugins' configured features, by identifier.</param>
/// <param name="PlacedFeatures">The plugins' placed features, by identifier.</param>
/// <param name="BiomeFeatures">The placed features of each biome's steps, by biome identifier. This is
/// <c>BiomeFeatures.All</c> itself when plugins change none.</param>
/// <param name="Owners">The plugins whose code each of the biomes' features runs, because it's made of their types,
/// for the features that run any. Placing them is a call into those plugins (see <see cref="PluginCall"/>).</param>
internal sealed record WorldgenFeatureSet(
    FrozenDictionary<string, ConfiguredFeatureBase> ConfiguredFeatures,
    FrozenDictionary<string, PlacedFeature> PlacedFeatures,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> BiomeFeatures,
    FrozenDictionary<PlacedFeature, ImmutableArray<PluginContainer>> Owners)
{
    internal Func<string, ConfiguredFeatureBase?>? ConfiguredLookup { get; init; }

    internal Func<string, PlacedFeature?>? PlacedLookup { get; init; }

    internal Func<JsonElement, ConfiguredFeatureBase>? InlineConfiguredLookup { get; init; }

    internal Func<JsonElement, PlacedFeature>? InlinePlacedLookup { get; init; }

    internal Func<string, StructureProcessorList>? ProcessorLookup { get; init; }

    internal Func<object, ImmutableArray<PluginContainer>>? OwnerLookup { get; init; }

    internal ImmutableArray<PluginContainer> OwnersOf(object entry) =>
        this.OwnerLookup?.Invoke(entry) ?? this.EntryOwners.GetValueOrDefault(entry, []);

    /// <summary>
    /// Vanilla's features alone.
    /// </summary>
    public static WorldgenFeatureSet Vanilla { get; } = new(
        FrozenDictionary<string, ConfiguredFeatureBase>.Empty,
        FrozenDictionary<string, PlacedFeature>.Empty,
        global::Obsidian.Registries.BiomeFeatures.All,
        FrozenDictionary<PlacedFeature, ImmutableArray<PluginContainer>>.Empty);

    /// <summary>
    /// The dimensions whose biomes' features can't be ordered even without biome modifiers, because their biomes'
    /// own features are in a cycle. They can't be generated with these features.
    /// </summary>
    public ImmutableArray<string> Unordered { get; init; } = [];

    /// <summary>
    /// The plugins' configured carvers, by identifier.
    /// </summary>
    public FrozenDictionary<string, IConfiguredCarver> ConfiguredCarvers { get; init; } =
        FrozenDictionary<string, IConfiguredCarver>.Empty;

    /// <summary>
    /// Each biome's configured carvers, by biome identifier: vanilla's biomes' (<c>ConfiguredCarvers.VanillaBiomes</c>
    /// itself when plugins change none), with plugins' biomes and carver modifiers applied.
    /// </summary>
    public IReadOnlyDictionary<string, ImmutableArray<IConfiguredCarver>> BiomeCarvers { get; init; } =
        WorldData.Generators.Mojang.Carvers.ConfiguredCarvers.VanillaBiomes;

    /// <summary>
    /// The processor lists of plugins' and data packs' files, by identifier, vanilla's overridden ones included.
    /// </summary>
    public FrozenDictionary<string, StructureProcessorList> ProcessorLists { get; init; } =
        FrozenDictionary<string, StructureProcessorList>.Empty;

    /// <summary>
    /// The plugins whose code each entry read from files runs (features and processor lists, and entries defined
    /// inline in them), for the entries that run any, so that structures using them call into those plugins.
    /// </summary>
    public FrozenDictionary<object, ImmutableArray<PluginContainer>> EntryOwners { get; init; } =
        FrozenDictionary<object, ImmutableArray<PluginContainer>>.Empty;

    /// <summary>
    /// Every plugin whose code any of the biomes' features runs.
    /// </summary>
    public ImmutableArray<PluginContainer> Plugins { get; } =
        [.. Owners.Values.SelectMany(owners => owners).Distinct()];

    /// <summary>
    /// Finds a configured feature by identifier, as vanilla's registry holds it: a plugin's or data pack's, an
    /// override of vanilla's, a vanilla feature read again around an override, or else vanilla's compiled one.
    /// </summary>
    /// <returns>The feature, or null when there's none of that identifier.</returns>
    public ConfiguredFeatureBase? FindConfigured(string identifier) =>
        this.ConfiguredLookup is not null ? this.ConfiguredLookup(identifier)
        : this.ConfiguredFeatures.GetValueOrDefault(identifier)
        ?? global::Obsidian.Registries.ConfiguredFeatures.All.GetValueOrDefault(identifier);

    /// <summary>
    /// Finds a placed feature by identifier, like <see cref="FindConfigured"/>: a plugin's or data pack's, an override
    /// of vanilla's, or else vanilla's compiled one.
    /// </summary>
    /// <returns>The feature, or null when there's none of that identifier.</returns>
    public PlacedFeature? FindPlaced(string identifier) =>
        this.PlacedLookup is not null ? this.PlacedLookup(identifier)
        : this.PlacedFeatures.GetValueOrDefault(identifier)
        ?? global::Obsidian.Registries.PlacedFeatures.All.GetValueOrDefault(identifier);

    /// <summary>
    /// Places a configured feature found by identifier (see <see cref="FindConfigured"/>), for what places a feature
    /// by name outside a biome's features, like the pale oak's moss decorator. Vanilla looks these up in its
    /// registry by key when they're placed, so files overriding them apply. A feature made of plugins' types is a
    /// call into those plugins, and isn't placed while one unloads.
    /// </summary>
    /// <returns>Whether the feature was found and placed something.</returns>
    /// <exception cref="PluginGenerationException">Placing a feature made of plugins' types failed.</exception>
    public bool PlaceConfigured(string identifier, FeatureContext context) =>
        this.FindConfigured(identifier) is ConfiguredFeatureBase feature
        && this.Place(feature, $"configured feature '{identifier}'", () => feature.Place(context));

    /// <summary>
    /// Places a placed feature at <paramref name="origin"/>, through its placement modifiers, like
    /// <see cref="PlaceConfigured"/>: a feature made of plugins' types is a call into those plugins, and isn't placed
    /// while one unloads.
    /// </summary>
    /// <returns>Whether the feature placed something.</returns>
    /// <exception cref="PluginGenerationException">Placing a feature made of plugins' types failed.</exception>
    public bool PlacePlaced(
        PlacedFeature feature,
        IWorldGenLevel level,
        WorldGenerationContext generation,
        IRandomSource random,
        Vector origin)
    {
        var name = feature.Identifier.Length > 0 ? $"placed feature '{feature.Identifier}'" : "a placed feature";

        return this.Place(feature, name, () => feature.Place(level, generation, random, origin));
    }

    // Runs a placement holding a call into the plugins whose types the entry is made of.
    private bool Place(object entry, string name, Func<bool> place)
    {
        var owners = this.OwnersOf(entry);
        if (owners.IsEmpty && entry is PlacedFeature placed)
            owners = this.Owners.GetValueOrDefault(placed, []);

        return Place(owners, name, place);
    }

    internal static bool Place(ImmutableArray<PluginContainer> owners, string name, Func<bool> place)
    {
        if (!GeneratorCalls.TryEnter(owners, out var calls))
            return false;

        using (calls)
        {
            try
            {
                return place();
            }
            catch (Exception exception) when (!owners.IsEmpty
                && exception is not (GeneratorStoppedException or PluginGenerationException or OutOfMemoryException))
            {
                throw new PluginGenerationException(owners, $"Failed to place {name}.", exception);
            }
        }
    }
}
