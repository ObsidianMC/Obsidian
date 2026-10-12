using Microsoft.Extensions.Logging;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Plugins;
using Obsidian.Plugins.PluginProviders;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Features;
using System.Collections.Frozen;
using System.IO;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Adds plugins' configured and placed features from their data files, like a data pack's, and applies their
/// NeoForge biome modifiers to the biomes' features.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>data/&lt;namespace&gt;/worldgen/configured_feature/&lt;path&gt;.json</c> and
/// <c>worldgen/placed_feature/...</c> are the features <c>&lt;namespace&gt;:&lt;path&gt;</c>, read with
/// <see cref="WorldgenReader"/>. They refer to vanilla's or plugins' features by identifier, or define them
/// inline.</item>
/// <item><c>data/&lt;namespace&gt;/neoforge/biome_modifier/&lt;path&gt;.json</c> is a biome modifier (see
/// <see cref="BiomeModifierData"/>).</item>
/// </list>
/// Vanilla's entries can't be overridden, and when plugins define the same entry, the first in load order wins.
/// Unlike biomes, features aren't sent to clients or saved, so they come and go with their plugin: each
/// <see cref="Apply"/> reads the loaded plugins' files into a new <see cref="WorldgenFeatureSet"/> and nothing keeps
/// the previous one's features once generators moved to the new one.
/// </remarks>
internal static partial class PluginFeatures
{
    public const string ConfiguredFeature = "worldgen/configured_feature";

    public const string PlacedFeature = "worldgen/placed_feature";

    public const string BiomeModifier = "neoforge/biome_modifier";

    /// <summary>
    /// The folders of plugins' feature and biome modifier files.
    /// </summary>
    public static FrozenSet<string> Registries { get; } =
        FrozenSet.Create(ConfiguredFeature, PlacedFeature, BiomeModifier);

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
    /// Finds the feature and biome modifier files in a plugin's files, by their paths.
    /// </summary>
    /// <returns>Each file's path, registry (one of <see cref="Registries"/>) and entry identifier.</returns>
    public static IEnumerable<(string Path, string Registry, string Identifier)> FindFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var parts = path.Split('/', 3);
            if (parts.Length < 3 || parts[0] != "data" || !path.EndsWith(".json", StringComparison.Ordinal))
                continue;

            foreach (var registry in Registries)
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
    /// Reads a feature or biome modifier file. Features are only checked to be JSON objects here: they're read when
    /// applied, since they may refer to other plugins' features.
    /// </summary>
    /// <param name="registry">One of <see cref="Registries"/>.</param>
    /// <param name="identifier">The entry's identifier, like <c>ruby:ore_ruby</c>.</param>
    /// <param name="source">Where the file comes from, for messages.</param>
    /// <exception cref="InvalidDataException">
    /// The file isn't valid, or it's for an entry in the <c>minecraft</c> namespace, which plugins can't override.
    /// </exception>
    public static FeatureFile Parse(string registry, string identifier, string source, ReadOnlySpan<byte> json)
    {
        if (!PluginTags.IsFileIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        if (identifier.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new InvalidDataException($"{source} would override vanilla's {identifier}, which plugins can't.");

        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), documentOptions);
            var root = CodecJson.Object(document.RootElement, "").Clone();

            var modifier = registry switch
            {
                BiomeModifier => BiomeModifierData.Read(root),
                ConfiguredFeature or PlacedFeature => null,
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
    /// <param name="files">The loaded plugins' feature and biome modifier files, in load order.</param>
    /// <param name="biomes">The loaded plugins' codec files, in load order, whose biomes' features are added.</param>
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
            var features = Build(files, biomes, types, logger);
            var broken = features.Unordered.FirstOrDefault(generating.Contains);

            if (broken is not null)
            {
                Log.KeptPrevious(logger, broken);
                return false;
            }

            Volatile.Write(ref current, features);
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
        IEnumerable<MojangDimension>? dimensions = null)
    {
        // The first plugin in load order defining an entry wins.
        var kept = new List<FeatureFile>();
        var byEntry = new Dictionary<(string Registry, string Identifier), FeatureFile>();

        foreach (var file in files)
        {
            if (byEntry.TryGetValue((file.Registry, file.Identifier), out var first))
            {
                Log.Duplicate(logger, file.Identifier, file.Source, first.Source);
                continue;
            }

            byEntry.Add((file.Registry, file.Identifier), file);
            kept.Add(file);
        }

        var resolver = new FeatureResolver(kept, WorldgenTypes.With(types));

        foreach (var file in kept.Where(file => file.Registry != BiomeModifier))
        {
            try
            {
                if (file.Registry == ConfiguredFeature)
                    resolver.Configured(file.Identifier);
                else
                    resolver.Placed(file.Identifier);
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file.Source, ex.Message);
            }
        }

        var biomeFeatures = new BiomeFeatureLists();

        foreach (var biome in PluginBiomes(biomes))
        {
            try
            {
                biomeFeatures.Set(biome.Identifier, resolver.BiomeSteps((BiomeElement)biome.Element));
            }
            catch (InvalidDataException ex)
            {
                Log.BiomeFeaturesLeftOut(logger, biome.Identifier, biome.Source, ex.Message);
            }
        }

        // Like NeoForge, every modifier adding features runs before any removing them, each in load order.
        var modifiers = kept.Where(file => file.Modifier is not null).ToList();
        var phases = modifiers.Where(file => file.Modifier!.Type == BiomeModifierData.AddFeatures)
            .Concat(modifiers.Where(file => file.Modifier!.Type == BiomeModifierData.RemoveFeatures));

        var unmodified = biomeFeatures.Snapshot();

        foreach (var file in phases)
        {
            try
            {
                biomeFeatures.Modify(file.Modifier!, resolver);
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file.Source, ex.Message);
            }
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
            Unordered = unordered
        };
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

    // The loaded plugins' biome files, the first in load order for each biome.
    private static IEnumerable<CodecFile> PluginBiomes(IEnumerable<CodecFile> files) => files
        .Where(file => file.Registry == PluginCodecs.Biome)
        .DistinctBy(file => file.Identifier);

    /// <summary>
    /// Reads plugins' features on demand, so that features can refer to each other in any order: each is read once
    /// and shared, and one that can't be read, or refers back to itself, fails every feature using it.
    /// </summary>
    private sealed class FeatureResolver
    {
        private readonly Dictionary<string, FeatureFile> configuredFiles;
        private readonly Dictionary<string, FeatureFile> placedFiles;
        private readonly Dictionary<FeatureFile, string> failed = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<FeatureFile> reading = new(ReferenceEqualityComparer.Instance);
        private readonly WorldgenReader reader;

        // The plugins whose types each feature being read is made of so far, the innermost feature's last.
        private readonly Stack<HashSet<PluginContainer>> owning = [];

        public FeatureResolver(List<FeatureFile> files, WorldgenTypes types)
        {
            this.configuredFiles = files
                .Where(file => file.Registry == ConfiguredFeature)
                .ToDictionary(file => file.Identifier);

            this.placedFiles = files
                .Where(file => file.Registry == PlacedFeature)
                .ToDictionary(file => file.Identifier);

            this.reader = new WorldgenReader(types, this.Configured, this.Placed, this.OnCreated);
        }

        public Dictionary<string, ConfiguredFeatureBase> ConfiguredFeatures { get; } = [];

        public Dictionary<string, PlacedFeature> PlacedFeatures { get; } = [];

        /// <summary>
        /// The plugins whose code each feature read runs: those whose types it, or a feature it refers to, is made
        /// of. Features made of Obsidian's types alone aren't in it.
        /// </summary>
        public Dictionary<object, ImmutableArray<PluginContainer>> Owners { get; } =
            new(ReferenceEqualityComparer.Instance);

        public ConfiguredFeatureBase Configured(string identifier)
        {
            if (global::Obsidian.Registries.ConfiguredFeatures.All.TryGetValue(identifier, out var vanilla))
                return vanilla;

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
            if (global::Obsidian.Registries.PlacedFeatures.All.TryGetValue(identifier, out var vanilla))
                return vanilla;

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

        /// <summary>
        /// A placed feature given as JSON: an identifier, or one defined inline, which is a new feature each time.
        /// </summary>
        public PlacedFeature Placed(JsonElement json, string path) =>
            json.ValueKind == JsonValueKind.String
                ? this.Placed(json.GetString()!)
                : this.Used(this.Track(() => this.reader.ReadPlacedFeature(json, null, path)));

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
                        throw new InvalidDataException($"{path}: placed feature tags aren't supported");

                    features.Add(entry.StartsWith('{')
                        ? this.Placed(ParseInline(entry, path), path)
                        : this.Placed(entry));
                }

                steps.Add(features);
            }

            return steps;
        }

        // The biome file was read leniently, so a feature's JSON text can have comments and trailing commas.
        private static JsonElement ParseInline(string json, string path)
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
                var feature = this.Track(read);
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

        // Reads a feature, noting the plugins whose types it's made of in Owners.
        private T Track<T>(Func<T> read)
            where T : class
        {
            var owners = new HashSet<PluginContainer>();
            this.owning.Push(owners);

            try
            {
                var feature = read();
                if (owners.Count > 0)
                    this.Owners[feature] = [.. owners];

                return feature;
            }
            finally
            {
                this.owning.Pop();
            }
        }

        // A feature used by the one being read, if any, which then runs its plugins' code too.
        private T Used<T>(T feature)
            where T : class
        {
            if (this.owning.TryPeek(out var user) && this.Owners.TryGetValue(feature, out var owners))
                user.UnionWith(owners);

            return feature;
        }

        // An object the reader created, which belongs to a plugin when its type is from the plugin's assembly.
        private void OnCreated(Type type)
        {
            if (this.owning.TryPeek(out var owners)
                && AssemblyLoadContext.GetLoadContext(type.Assembly) is PluginLoadContext
                {
                    Container: PluginContainer owner
                })
            {
                owners.Add(owner);
            }
        }
    }

    /// <summary>
    /// The biomes' features per decoration step, starting from vanilla's <c>BiomeFeatures.All</c>, which stays
    /// the result as long as nothing changes it.
    /// </summary>
    private sealed class BiomeFeatureLists
    {
        private readonly Dictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> lists =
            new(global::Obsidian.Registries.BiomeFeatures.All);

        private bool changed;

        public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> Result =>
            this.changed ? this.lists.ToFrozenDictionary() : global::Obsidian.Registries.BiomeFeatures.All;

        /// <summary>
        /// Whether any biome's features differ from vanilla's.
        /// </summary>
        public bool Changed => this.changed;

        /// <summary>
        /// Gives a biome its features.
        /// </summary>
        public void Set(string biome, List<List<PlacedFeature>> steps)
        {
            if (steps.Count == 0)
                return;

            this.lists[biome] = steps;
            this.changed = true;
        }

        /// <summary>
        /// Applies a biome modifier. Its features' order isn't checked here: a later modifier may still change it.
        /// </summary>
        public void Modify(BiomeModifierData modifier, FeatureResolver resolver)
        {
            var biomes = ResolveBiomes(modifier.Biomes);
            var features = modifier.Features
                .Select((feature, i) => resolver.Placed(feature, $"features[{i}]"))
                .ToList();

            var removed = features.ToHashSet(ReferenceEqualityComparer.Instance);

            foreach (var biome in biomes)
            {
                var steps = this.lists.GetValueOrDefault(biome)?.Select(step => step.ToList()).ToList() ?? [];

                foreach (var step in modifier.Steps.Select(step => (int)step))
                {
                    if (modifier.Type == BiomeModifierData.AddFeatures)
                    {
                        while (steps.Count <= step)
                            steps.Add([]);

                        steps[step].AddRange(features);
                    }
                    else if (step < steps.Count)
                    {
                        steps[step].RemoveAll(feature => removed.Contains(feature));
                    }
                }

                this.lists[biome] = steps;
            }

            this.changed |= biomes.Count > 0;
        }

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
                FeatureSorter.Build(dimension.PossibleBiomes, biome => this.lists.GetValueOrDefault(biome.Name) ?? []);
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

            foreach (var biome in dimension.PossibleBiomes)
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

        // A holder set of biomes to their identifiers: vanilla's or plugins' biomes, or a vanilla biome tag.
        private static List<string> ResolveBiomes(ImmutableArray<string> biomes)
        {
            if (biomes is [var only] && only.StartsWith('#'))
            {
                if (!TagsRegistry.TryGet("worldgen/biome", only[1..], out var tag))
                    throw new InvalidDataException($"biomes: {only} isn't a biome tag");

                return [.. tag.Entries
                    .Select(id => CodecRegistry.TryGetBiome(id, out var biome) ? biome!.Name : null)
                    .OfType<string>()];
            }

            foreach (var biome in biomes)
            {
                if (!CodecRegistry.TryGetBiome(biome, out _))
                    throw new InvalidDataException($"biomes: {biome} isn't a biome");
            }

            return [.. biomes.Distinct()];
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Left out {Identifier} from {Source}: {First} defined it first")]
        public static partial void Duplicate(ILogger logger, string identifier, string source, string first);

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
/// A plugin's configured feature, placed feature or biome modifier file.
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
    BiomeModifierData? Modifier);

/// <summary>
/// The features chunk generation places: the loaded plugins' features, and every biome's features per decoration
/// step, which are vanilla's with plugins' biomes and biome modifiers applied.
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
    /// Every plugin whose code any of the biomes' features runs.
    /// </summary>
    public ImmutableArray<PluginContainer> Plugins { get; } =
        [.. Owners.Values.SelectMany(owners => owners).Distinct()];
}
