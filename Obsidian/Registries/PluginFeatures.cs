using Microsoft.Extensions.Logging;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang.Features;
using System.Collections.Frozen;
using System.IO;
using System.Reflection;
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
        if (!PluginTags.IsIdentifier(identifier))
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
    /// <param name="assemblies">The loaded plugins' assemblies, whose worldgen types files can name.</param>
    public static void Apply(
        IEnumerable<FeatureFile> files,
        IEnumerable<CodecFile> biomes,
        IEnumerable<Assembly> assemblies,
        ILogger logger)
    {
        lock (applying)
            Volatile.Write(ref current, Build(files, biomes, assemblies, logger));
    }

    /// <summary>
    /// Reads plugins' files into a feature set, without making it the current one. Files that can't be read are
    /// logged and left out, with the features and modifiers that need them.
    /// </summary>
    public static WorldgenFeatureSet Build(
        IEnumerable<FeatureFile> files,
        IEnumerable<CodecFile> biomes,
        IEnumerable<Assembly> assemblies,
        ILogger logger)
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

        var resolver = new FeatureResolver(kept, WorldgenTypes.With(assemblies));

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

        return new WorldgenFeatureSet(
            resolver.ConfiguredFeatures.ToFrozenDictionary(),
            resolver.PlacedFeatures.ToFrozenDictionary(),
            biomeFeatures.Result);
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

        public FeatureResolver(List<FeatureFile> files, WorldgenTypes types)
        {
            this.configuredFiles = files
                .Where(file => file.Registry == ConfiguredFeature)
                .ToDictionary(file => file.Identifier);

            this.placedFiles = files
                .Where(file => file.Registry == PlacedFeature)
                .ToDictionary(file => file.Identifier);

            this.reader = new WorldgenReader(types, this.Configured, this.Placed);
        }

        public Dictionary<string, ConfiguredFeatureBase> ConfiguredFeatures { get; } = [];

        public Dictionary<string, PlacedFeature> PlacedFeatures { get; } = [];

        public ConfiguredFeatureBase Configured(string identifier)
        {
            if (global::Obsidian.Registries.ConfiguredFeatures.All.TryGetValue(identifier, out var vanilla))
                return vanilla;

            if (this.ConfiguredFeatures.TryGetValue(identifier, out var feature))
                return feature;

            if (!this.configuredFiles.TryGetValue(identifier, out var file))
                throw new InvalidDataException($"{identifier} isn't a configured feature");

            return this.Read(
                file,
                this.ConfiguredFeatures,
                () => this.reader.ReadConfiguredFeature(file.Json, file.Identifier));
        }

        public PlacedFeature Placed(string identifier)
        {
            if (global::Obsidian.Registries.PlacedFeatures.All.TryGetValue(identifier, out var vanilla))
                return vanilla;

            if (this.PlacedFeatures.TryGetValue(identifier, out var feature))
                return feature;

            if (!this.placedFiles.TryGetValue(identifier, out var file))
                throw new InvalidDataException($"{identifier} isn't a placed feature");

            return this.Read(
                file,
                this.PlacedFeatures,
                () => this.reader.ReadPlacedFeature(file.Json, file.Identifier));
        }

        /// <summary>
        /// A placed feature given as JSON: an identifier, or one defined inline, which is a new feature each time.
        /// </summary>
        public PlacedFeature Placed(JsonElement json, string path) =>
            json.ValueKind == JsonValueKind.String
                ? this.Placed(json.GetString()!)
                : this.reader.ReadPlacedFeature(json, null, path);

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

                    if (!entry.StartsWith('{'))
                    {
                        features.Add(this.Placed(entry));
                        continue;
                    }

                    using var document = JsonDocument.Parse(entry);
                    features.Add(this.reader.ReadPlacedFeature(document.RootElement, null, path));
                }

                steps.Add(features);
            }

            return steps;
        }

        private T Read<T>(FeatureFile file, Dictionary<string, T> features, Func<T> read)
        {
            if (this.failed.ContainsKey(file))
                throw new InvalidDataException($"{file.Identifier} isn't valid");

            if (!this.reading.Add(file))
                throw new InvalidDataException($"{file.Identifier} refers back to itself");

            try
            {
                var feature = read();
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
        /// Gives a biome its features, unless they'd make the features' order a cycle.
        /// </summary>
        public void Set(string biome, List<List<PlacedFeature>> steps)
        {
            if (steps.Count == 0)
                return;

            var previous = this.lists.GetValueOrDefault(biome);
            this.lists[biome] = steps;

            if (this.HasCycle())
            {
                Restore(this.lists, biome, previous);
                throw new InvalidDataException(CycleMessage);
            }

            this.changed = true;
        }

        public void Modify(BiomeModifierData modifier, FeatureResolver resolver)
        {
            var biomes = ResolveBiomes(modifier.Biomes);
            var features = modifier.Features
                .Select((feature, i) => resolver.Placed(feature, $"features[{i}]"))
                .ToList();

            var removed = features.ToHashSet(ReferenceEqualityComparer.Instance);
            var previous = biomes.ToDictionary(biome => biome, this.lists.GetValueOrDefault);

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

            // Vanilla can't order features that two biomes list in opposite orders, and stops; this modifier is
            // left out instead.
            if (this.HasCycle())
            {
                foreach (var (biome, steps) in previous)
                    Restore(this.lists, biome, steps);

                throw new InvalidDataException(CycleMessage);
            }

            this.changed |= biomes.Count > 0;
        }

        private const string CycleMessage = "the biomes' features would be in a cycle: one feature would have to "
            + "be placed both before and after another";

        // Whether the features' order over every biome has a cycle, which FeatureSorter rejects. A dimension's
        // biomes are a subset, so they can then always be sorted.
        private bool HasCycle()
        {
            try
            {
                FeatureSorter.Build(this.lists.Keys.ToList(), biome => this.lists[biome]);
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private static void Restore(
            Dictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> lists,
            string biome,
            IReadOnlyList<IReadOnlyList<PlacedFeature>>? steps)
        {
            if (steps is null)
                lists.Remove(biome);
            else
                lists[biome] = steps;
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
internal sealed record WorldgenFeatureSet(
    FrozenDictionary<string, ConfiguredFeatureBase> ConfiguredFeatures,
    FrozenDictionary<string, PlacedFeature> PlacedFeatures,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> BiomeFeatures)
{
    /// <summary>
    /// Vanilla's features alone.
    /// </summary>
    public static WorldgenFeatureSet Vanilla { get; } = new(
        FrozenDictionary<string, ConfiguredFeatureBase>.Empty,
        FrozenDictionary<string, PlacedFeature>.Empty,
        global::Obsidian.Registries.BiomeFeatures.All);
}
