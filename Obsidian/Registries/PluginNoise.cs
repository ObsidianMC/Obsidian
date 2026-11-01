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
/// which the 26.3 pipeline reads (see <see cref="PluginWorldgen"/>).
/// </summary>
/// <remarks>
/// A file for one of vanilla's entries overrides it; when files define the same entry, the last in order wins (see
/// <see cref="VanillaOverrides"/>). Each <see cref="Apply"/> keeps the files in a new <see cref="WorldgenNoise"/>. A
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
    /// Keeps the files that apply, the last of each entry's, without making them the current ones.
    /// </summary>
    public static WorldgenNoise Build(
        IEnumerable<NoiseFile> files,
        IEnumerable<WorldgenTypeRegistration> types,
        ILogger logger)
    {
        var kept = VanillaOverrides.LastWins(
            files,
            file => (file.Registry, file.Identifier),
            file => file.Source,
            logger);

        return new WorldgenNoise(WorldgenTypes.With(types)) { Files = kept.ToArray() };
    }

    /// <summary>
    /// Reads a file of any namespace, such as vanilla's own.
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
/// The noise settings, density functions, noises and parameter lists of the loaded plugins' and data packs' files, which
/// world generation reads over vanilla's.
/// </summary>
/// <param name="types">The worldgen types the files are read with: Obsidian's and the loaded plugins'.</param>
internal sealed class WorldgenNoise(WorldgenTypes types)
{
    /// <summary>
    /// Vanilla's entries alone.
    /// </summary>
    public static WorldgenNoise Vanilla { get; } = new(WorldgenTypes.BuiltIn);

    /// <summary>
    /// The worldgen types the entries are read with: Obsidian's and the loaded plugins'.
    /// </summary>
    public WorldgenTypes Types { get; } = types;

    /// <summary>
    /// The files, as written, which the 26.3 pipeline reads (see <see cref="PluginWorldgen"/>).
    /// </summary>
    public IReadOnlyList<NoiseFile> Files { get; init; } = [];

    /// <summary>
    /// Finds a multi-noise parameter list by identifier: a file's, or vanilla's.
    /// </summary>
    public bool TryGetParameterList(string identifier, [NotNullWhen(true)] out ParameterListPreset? list)
    {
        var file = this.Files.LastOrDefault(file => file.Registry == PluginNoise.ParameterList
            && file.Identifier == identifier);
        if (file is not null)
        {
            list = ParameterListPreset.Read(file.Json, identifier);
            return true;
        }

        return ParameterListPreset.Vanilla.TryGetValue(identifier, out list);
    }
}

/// <summary>
/// A multi-noise parameter list (<c>worldgen/multi_noise_biome_source_parameter_list</c>), which in vanilla only
/// names one of its built-in presets: <c>{"preset": "minecraft:overworld"}</c> or <c>minecraft:nether</c>.
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
