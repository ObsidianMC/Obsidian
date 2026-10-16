using Microsoft.Extensions.Logging;
using Obsidian.API.World.Generator.Noise;
using Obsidian.Plugins;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Features;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Plugins' and data packs' noise settings, density functions, noises and multi-noise parameter lists, from their data
/// files: <c>data/&lt;namespace&gt;/worldgen/noise_settings/&lt;path&gt;.json</c>,
/// <c>worldgen/density_function/...</c>, <c>worldgen/noise/...</c> and
/// <c>worldgen/multi_noise_biome_source_parameter_list/...</c> are the entries <c>&lt;namespace&gt;:&lt;path&gt;</c>,
/// read with <see cref="WorldgenReader"/> into the same objects the source generator makes of vanilla's.
/// </summary>
/// <remarks>
/// Entries refer to vanilla's or files' entries by identifier, in any order; each is read once and shared, and one
/// that refers back to itself is left out. A file for one of vanilla's entries overrides it, and the vanilla entries
/// naming it are read again from vanilla's files (see <see cref="VanillaOverrides"/>); when files define the same
/// entry, the last in order wins. Each <see cref="Apply"/> reads the files into a new <see cref="WorldgenNoise"/>. A
/// dimension keeps the noise settings it was read with (see <see cref="PluginDimensions"/>), so changes apply to
/// dimensions added later, or after a restart.
/// </remarks>
internal static partial class PluginNoise
{
    public const string NoiseSettings = "worldgen/noise_settings";

    public const string DensityFunction = "worldgen/density_function";

    public const string Noise = "worldgen/noise";

    public const string ParameterList = "worldgen/multi_noise_biome_source_parameter_list";

    /// <summary>
    /// The folders of noise settings, density function, noise and parameter list files.
    /// </summary>
    public static FrozenSet<string> Registries { get; } =
        FrozenSet.Create(NoiseSettings, DensityFunction, Noise, ParameterList);

    private static readonly Lock applying = new();

    private static WorldgenNoise? current;

    // Gson reads data packs leniently, so comments and trailing commas are fine.
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// The entries plugins' dimensions are read with: vanilla's and the loaded plugins'.
    /// </summary>
    public static WorldgenNoise Current => Volatile.Read(ref current) ?? WorldgenNoise.Vanilla;

    /// <summary>
    /// Finds the noise settings, density function and noise files in a plugin's files, by their paths.
    /// </summary>
    /// <returns>Each file's path, registry (one of <see cref="Registries"/>) and entry identifier.</returns>
    public static IEnumerable<(string Path, string Registry, string Identifier)> FindFiles(IEnumerable<string> paths) =>
        PluginFeatures.FindFiles(paths, Registries);

    /// <summary>
    /// Reads a noise settings, density function or noise file. Entries are only checked to be JSON here: they're read
    /// when applied, since they may refer to other plugins' entries.
    /// </summary>
    /// <param name="registry">One of <see cref="Registries"/>.</param>
    /// <param name="identifier">The entry's identifier, like <c>ruby:crystal</c>.</param>
    /// <param name="source">Where the file comes from, for messages.</param>
    /// <exception cref="InvalidDataException">The file isn't valid.</exception>
    public static NoiseFile Parse(string registry, string identifier, string source, ReadOnlySpan<byte> json)
    {
        if (!Registries.Contains(registry))
            throw new ArgumentException($"{registry} isn't read from plugins' noise files.", nameof(registry));

        if (!PluginTags.IsFileIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        return Create(registry, identifier, source, json);
    }

    /// <summary>
    /// Makes the entries of the loaded plugins' files the <see cref="Current"/> ones.
    /// </summary>
    /// <param name="files">The loaded plugins' files in load order, then data packs' files in theirs.</param>
    /// <param name="types">The worldgen types the loaded plugins registered, which files can name.</param>
    public static void Apply(
        IEnumerable<NoiseFile> files,
        IEnumerable<WorldgenTypeRegistration> types,
        ILogger logger)
    {
        lock (applying)
            Volatile.Write(ref current, Build(files, types, logger));
    }

    /// <summary>
    /// Reads plugins' files into a set of entries, without making it the current one. Files that can't be read are
    /// logged and left out, with the entries that need them.
    /// </summary>
    /// <param name="builtIn">Whether vanilla's generated entries are found too. Without them, entries are only
    /// those of <paramref name="files"/>, which may then be vanilla's own files.</param>
    public static WorldgenNoise Build(
        IEnumerable<NoiseFile> files,
        IEnumerable<WorldgenTypeRegistration> types,
        ILogger logger,
        bool builtIn = true)
    {
        var kept = VanillaOverrides.LastWins(
            files,
            file => (file.Registry, file.Identifier),
            file => file.Source,
            logger);

        if (builtIn)
            kept.AddRange(VanillaDependents(kept));

        var resolver = new NoiseResolver(kept, WorldgenTypes.With(types), builtIn);

        foreach (var file in kept)
        {
            try
            {
                resolver.Resolve(file);
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file.Source, ex.Message);
            }
        }

        return resolver.Result();
    }

    // Vanilla's entries naming an overridden one, read again from vanilla's files so that they use the override.
    private static IEnumerable<NoiseFile> VanillaDependents(List<NoiseFile> files)
    {
        // Parameter lists are only named by level stems.
        var overridden = files
            .Where(file => file.Registry != ParameterList)
            .Select(file => (file.Registry, file.Identifier))
            .ToHashSet();

        if (!overridden.Any(entry => entry.Identifier.StartsWith("minecraft:", StringComparison.Ordinal)))
            return [];

        return VanillaOverrides.Dependents([NoiseSettings, DensityFunction, Noise], overridden)
            .Select(entry => new NoiseFile(
                entry.Registry,
                entry.Identifier,
                $"vanilla's {entry.Identifier}",
                entry.Json,
                entry.Json.GetRawText()));
    }

    /// <summary>
    /// Reads a file of any namespace, such as vanilla's own, for <see cref="Build"/> without built-in entries.
    /// </summary>
    internal static NoiseFile Create(string registry, string identifier, string source, ReadOnlySpan<byte> json)
    {
        try
        {
            var text = Encoding.UTF8.GetString(json);
            using var document = JsonDocument.Parse(text, documentOptions);

            // A density function file may be a number or an identifier too.
            var root = registry == DensityFunction
                ? document.RootElement.Clone()
                : CodecJson.Object(document.RootElement, "").Clone();

            return new NoiseFile(registry, identifier, source, root, text);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"{source} isn't a valid {registry} file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads entries on demand, so that they can refer to each other in any order: each is read once and shared, and
    /// one that can't be read, or refers back to itself, fails every entry using it.
    /// </summary>
    private sealed class NoiseResolver
    {
        private readonly Dictionary<(string Registry, string Identifier), NoiseFile> files;
        private readonly Dictionary<NoiseFile, string> failed = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<NoiseFile> reading = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, NoiseSetting> settings = [];
        private readonly Dictionary<string, IDensityFunction> functions = [];
        private readonly Dictionary<string, BaseNoise> noises = [];
        private readonly Dictionary<string, ParameterListPreset> parameterLists = [];
        private readonly WorldgenTypes types;
        private readonly bool builtIn;
        private readonly WorldgenReader reader;

        // The plugins whose types each entry is made of, and the files it's read from, its own included.
        private readonly WorldgenDependencies<object> dependencies = new();

        public NoiseResolver(List<NoiseFile> files, WorldgenTypes types, bool builtIn)
        {
            this.files = files.ToDictionary(file => (file.Registry, file.Identifier));
            this.types = types;
            this.builtIn = builtIn;
            this.reader = new WorldgenReader(
                types,
                NoFeature<ConfiguredFeatureBase>,
                NoFeature<PlacedFeature>,
                this.OnCreated,
                this.Function,
                this.NoiseOf);
        }

        public void Resolve(NoiseFile file)
        {
            _ = file.Registry switch
            {
                NoiseSettings => (object)this.Settings(file.Identifier),
                DensityFunction => this.Function(file.Identifier),
                ParameterList => this.ParameterListOf(file.Identifier),
                _ => this.NoiseOf(file.Identifier)
            };
        }

        public WorldgenNoise Result() => new(
            this.settings.ToFrozenDictionary(),
            this.functions.ToFrozenDictionary(),
            this.noises.ToFrozenDictionary(),
            this.dependencies.Of.ToFrozenDictionary(ReferenceEqualityComparer.Instance),
            this.types,
            this.builtIn)
        {
            ParameterLists = this.parameterLists.ToFrozenDictionary()
        };

        // Vanilla's compiled entry, unless a file overrides it or it was read again because it names an override.
        private bool TryVanilla<T>(
            IReadOnlyDictionary<string, T> vanilla,
            string registry,
            string identifier,
            [NotNullWhen(true)] out T? entry)
        {
            entry = default;

            return this.builtIn
                && !this.files.ContainsKey((registry, identifier))
                && vanilla.TryGetValue(identifier, out entry);
        }

        private NoiseSetting Settings(string identifier)
        {
            if (this.TryVanilla(NoiseRegistry.NoiseSettings.All, NoiseSettings, identifier, out var vanilla))
                return vanilla;

            return this.Entry(
                NoiseSettings,
                identifier,
                this.settings,
                file => this.reader.Read<NoiseSetting>(file.Json),
                "noise settings");
        }

        private IDensityFunction Function(string identifier)
        {
            if (this.TryVanilla(NoiseRegistry.DensityFunctions.All, DensityFunction, identifier, out var vanilla))
                return vanilla;

            return this.Entry(
                DensityFunction,
                identifier,
                this.functions,
                file => this.reader.Read<IDensityFunction>(file.Json),
                "a density function");
        }

        private BaseNoise NoiseOf(string identifier)
        {
            if (this.TryVanilla(NoiseRegistry.Noises.All, Noise, identifier, out var vanilla))
                return vanilla;

            return this.Entry(
                Noise,
                identifier,
                this.noises,
                file => WorldgenReader.ReadNoise(file.Json, file.Identifier),
                "a noise");
        }

        private ParameterListPreset ParameterListOf(string identifier)
        {
            if (this.TryVanilla(ParameterListPreset.Vanilla, ParameterList, identifier, out var vanilla))
                return vanilla;

            return this.Entry(
                ParameterList,
                identifier,
                this.parameterLists,
                file => ParameterListPreset.Read(file.Json),
                "a parameter list");
        }

        private T Entry<T>(
            string registry,
            string identifier,
            Dictionary<string, T> entries,
            Func<NoiseFile, T> read,
            string kind)
            where T : class
        {
            if (!entries.TryGetValue(identifier, out var entry))
            {
                if (!this.files.TryGetValue((registry, identifier), out var file))
                    throw new InvalidDataException($"{identifier} isn't {kind}");

                entry = this.Read(file, entries, read);
            }

            return this.dependencies.Used(entry);
        }

        private T Read<T>(NoiseFile file, Dictionary<string, T> entries, Func<NoiseFile, T> read)
            where T : class
        {
            if (this.failed.ContainsKey(file))
                throw new InvalidDataException($"{file.Identifier} isn't valid");

            if (!this.reading.Add(file))
                throw new InvalidDataException($"{file.Identifier} refers back to itself");

            try
            {
                var entry = this.dependencies.Track(() =>
                {
                    this.dependencies.Add(file);
                    return read(file);
                });

                entries[file.Identifier] = entry;
                return entry;
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

        // An object the reader created, which belongs to a plugin when its type is from the plugin's assembly.
        private void OnCreated(Type type)
        {
            if (WorldgenDependencies<object>.Owner(type) is PluginContainer owner)
                this.dependencies.Add(owner);
        }

        private static T NoFeature<T>(string identifier) =>
            throw new InvalidDataException($"{identifier}: features can't be used here");
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Left out {Source}: {Message}")]
        public static partial void Invalid(ILogger logger, string source, string message);
    }
}

/// <summary>
/// A plugin's or data pack's noise settings, density function, noise or parameter list file.
/// </summary>
/// <param name="Registry">One of <see cref="PluginNoise.Registries"/>.</param>
/// <param name="Identifier">The entry's identifier, like <c>ruby:crystal</c>.</param>
/// <param name="Source">Where the file comes from, for messages.</param>
/// <param name="Json">The file's JSON.</param>
/// <param name="Text">The file's text, to tell a changed file on reload.</param>
internal sealed record NoiseFile(string Registry, string Identifier, string Source, JsonElement Json, string Text);

/// <summary>
/// The noise settings, density functions and noises world generation can use: vanilla's and the loaded plugins'.
/// </summary>
internal sealed class WorldgenNoise
{
    private readonly FrozenDictionary<object, ImmutableArray<object>> dependencies;
    private readonly bool builtIn;

    /// <param name="dependencies">The plugins whose types each entry is made of, and the files it's read from.</param>
    /// <param name="builtIn">Whether vanilla's generated entries are found too.</param>
    public WorldgenNoise(
        FrozenDictionary<string, NoiseSetting> noiseSettings,
        FrozenDictionary<string, IDensityFunction> densityFunctions,
        FrozenDictionary<string, BaseNoise> noises,
        FrozenDictionary<object, ImmutableArray<object>> dependencies,
        WorldgenTypes types,
        bool builtIn = true)
    {
        this.NoiseSettings = noiseSettings;
        this.DensityFunctions = densityFunctions;
        this.Noises = noises;
        this.dependencies = dependencies;
        this.Types = types;
        this.builtIn = builtIn;
    }

    /// <summary>
    /// Vanilla's entries alone.
    /// </summary>
    public static WorldgenNoise Vanilla { get; } = new(
        FrozenDictionary<string, NoiseSetting>.Empty,
        FrozenDictionary<string, IDensityFunction>.Empty,
        FrozenDictionary<string, BaseNoise>.Empty,
        FrozenDictionary<object, ImmutableArray<object>>.Empty,
        WorldgenTypes.BuiltIn);

    /// <summary>
    /// The plugins' noise settings, by identifier.
    /// </summary>
    public FrozenDictionary<string, NoiseSetting> NoiseSettings { get; }

    /// <summary>
    /// The plugins' density functions, by identifier.
    /// </summary>
    public FrozenDictionary<string, IDensityFunction> DensityFunctions { get; }

    /// <summary>
    /// The plugins' noises, by identifier.
    /// </summary>
    public FrozenDictionary<string, BaseNoise> Noises { get; }

    /// <summary>
    /// The multi-noise parameter lists files define, vanilla's overridden ones included.
    /// </summary>
    public FrozenDictionary<string, ParameterListPreset> ParameterLists { get; init; } =
        FrozenDictionary<string, ParameterListPreset>.Empty;

    /// <summary>
    /// The worldgen types the entries were read with: Obsidian's and the loaded plugins'.
    /// </summary>
    public WorldgenTypes Types { get; }

    /// <summary>
    /// Finds noise settings, vanilla's or a plugin's.
    /// </summary>
    public bool TryGetNoiseSettings(string identifier, [NotNullWhen(true)] out NoiseSetting? settings) =>
        this.NoiseSettings.TryGetValue(identifier, out settings)
        || this.builtIn && NoiseRegistry.NoiseSettings.All.TryGetValue(identifier, out settings);

    /// <summary>
    /// The plugins whose types an entry is made of, counting the entries it refers to.
    /// </summary>
    /// <summary>
    /// Finds a multi-noise parameter list by identifier: a file's, or vanilla's.
    /// </summary>
    public bool TryGetParameterList(string identifier, [NotNullWhen(true)] out ParameterListPreset? list) =>
        this.ParameterLists.TryGetValue(identifier, out list)
        || this.builtIn && ParameterListPreset.Vanilla.TryGetValue(identifier, out list);

    public ImmutableArray<PluginContainer> Owners(object entry) =>
        [.. this.Dependencies(entry).OfType<PluginContainer>()];

    /// <summary>
    /// The text of the plugin files entries are read from, counting the entries they refer to, to tell when a reload
    /// changes them; empty for vanilla's.
    /// </summary>
    public string Sources(IEnumerable<object> entries) => string.Concat(entries
        .SelectMany(this.Dependencies)
        .OfType<NoiseFile>()
        .Distinct()
        .OrderBy(file => file.Registry, StringComparer.Ordinal)
        .ThenBy(file => file.Identifier, StringComparer.Ordinal)
        .Select(file => $"{file.Registry} {file.Identifier}\n{file.Text}\n"));

    /// <summary>
    /// A reader of other data, like a level stem's biome source, whose density functions and noises are these.
    /// </summary>
    /// <param name="owners">Gets the plugins whose types what's read is made of: those of the objects it creates,
    /// and the owners of the entries it refers to.</param>
    /// <param name="entries">Gets the density functions and noises what's read refers to.</param>
    public WorldgenReader Reader(ISet<PluginContainer>? owners = null, ISet<object>? entries = null) => new(
        this.Types,
        identifier => throw new InvalidDataException($"{identifier}: features can't be used here"),
        identifier => throw new InvalidDataException($"{identifier}: features can't be used here"),
        type =>
        {
            if (WorldgenDependencies<PluginContainer>.Owner(type) is PluginContainer owner)
                owners?.Add(owner);
        },
        identifier => this.Referenced(this.FindDensityFunction(identifier), owners, entries),
        identifier => this.Referenced(this.FindNoise(identifier), owners, entries));

    // An entry referred to, which brings its owners along: generating with it runs their code.
    private T Referenced<T>(T entry, ISet<PluginContainer>? owners, ISet<object>? entries)
        where T : notnull
    {
        owners?.UnionWith(this.Owners(entry));
        entries?.Add(entry);

        return entry;
    }

    private IEnumerable<object> Dependencies(object entry) =>
        this.dependencies.TryGetValue(entry, out var dependencies) ? dependencies : [];

    private IDensityFunction FindDensityFunction(string identifier)
    {
        if (this.DensityFunctions.TryGetValue(identifier, out var function)
            || this.builtIn && NoiseRegistry.DensityFunctions.All.TryGetValue(identifier, out function))
        {
            return function;
        }

        throw new InvalidDataException($"{identifier} isn't a density function");
    }

    private BaseNoise FindNoise(string identifier)
    {
        if (this.Noises.TryGetValue(identifier, out var noise)
            || this.builtIn && NoiseRegistry.Noises.All.TryGetValue(identifier, out noise))
        {
            return noise;
        }

        throw new InvalidDataException($"{identifier} isn't a noise");
    }
}

/// <summary>
/// A multi-noise parameter list (<c>worldgen/multi_noise_biome_source_parameter_list</c>), which in vanilla 1.21.11
/// only names one of its built-in presets: <c>{"preset": "minecraft:overworld"}</c> or <c>minecraft:nether</c>.
/// </summary>
/// <param name="preset">The preset's identifier.</param>
internal sealed class ParameterListPreset(string preset)
{
    /// <summary>
    /// Vanilla's parameter lists, each its own preset.
    /// </summary>
    public static FrozenDictionary<string, ParameterListPreset> Vanilla { get; } =
        new Dictionary<string, ParameterListPreset>
        {
            ["minecraft:overworld"] = new("minecraft:overworld"),
            ["minecraft:nether"] = new("minecraft:nether")
        }.ToFrozenDictionary();

    /// <summary>
    /// The preset's identifier, <c>minecraft:overworld</c> or <c>minecraft:nether</c>.
    /// </summary>
    public string Preset { get; } = preset;

    /// <summary>
    /// Reads a parameter list, given in a file or inline in a level stem, like vanilla's codec.
    /// </summary>
    /// <exception cref="InvalidDataException">It isn't <c>{"preset": ...}</c> naming a preset.</exception>
    public static ParameterListPreset Read(JsonElement json, string path = "")
    {
        CodecJson.Object(json, path);

        var presetPath = CodecJson.Field(path, "preset");
        var preset = CodecJson.Identifier(CodecJson.Required(json, path, "preset"), presetPath);

        return Vanilla.TryGetValue(preset, out var list)
            ? new ParameterListPreset(list.Preset)
            : throw CodecJson.Error(
                presetPath,
                $"{preset} isn't a parameter list preset: expected minecraft:overworld or minecraft:nether");
    }
}
