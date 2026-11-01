using Microsoft.Extensions.Logging;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.DamageTypes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Nbt;
using Obsidian.Registries.Codecs;
using System.Collections.Frozen;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Adds plugins' and data packs' biomes, dimension types and damage types from their data files: a file
/// <c>data/&lt;namespace&gt;/worldgen/biome/&lt;path&gt;.json</c> (or <c>dimension_type</c>, <c>damage_type</c>)
/// is the entry <c>&lt;namespace&gt;:&lt;path&gt;</c>.
/// </summary>
/// <remarks>
/// Entries are added to the codec registries after vanilla's (see <see cref="CodecTable{TCodec}"/>) and stay until the
/// server stops, even once their plugin unloads, since chunks and saves refer to them. A file for one of vanilla's
/// entries overrides it until no file does any more, and when files define the same entry, the last in order wins
/// (see <see cref="VanillaOverrides.LastWins"/>).
/// <para>
/// A file is read the way vanilla 26.3 reads a data pack's entry with the registry's codec, and rejected where
/// vanilla would reject it (see <see cref="BiomeData"/>, <see cref="DimensionTypeData"/> and
/// <see cref="DamageTypeData"/>). Clients are sent files' entries as vanilla's network codecs write the read values,
/// so they always decode them, and vanilla's entries no file overrides by name.
/// </para>
/// </remarks>
internal static partial class PluginCodecs
{
    public const string Biome = "worldgen/biome";

    public const string DimensionType = "dimension_type";

    public const string DamageType = "damage_type";

    /// <summary>
    /// The registries plugins' data files may add to, by their folder in a data pack.
    /// </summary>
    public static FrozenSet<string> Registries { get; } = FrozenSet.Create(Biome, DimensionType, DamageType);

    private static readonly Lock applying = new();

    /// <summary>
    /// How many times applying plugins' files added an entry or changed one's data, so a client can tell it was
    /// configured with older entries.
    /// </summary>
    public static int Revision => Volatile.Read(ref revision);

    private static int revision;

    private static IReadOnlyList<CodecFile> applied = [];

    /// <summary>
    /// The files whose entries apply, from the last <see cref="Apply"/>: the 26.3 pipeline reads their biomes and
    /// dimension types from them (see <see cref="PluginWorldgen"/>).
    /// </summary>
    public static IReadOnlyList<CodecFile> Applied => Volatile.Read(ref applied);

    // Gson reads data packs leniently, so comments and trailing commas are fine.
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Finds the data files of <see cref="Registries"/> in a plugin's files, by their paths.
    /// </summary>
    /// <returns>Each file's path, registry and entry identifier; other files are left out.</returns>
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
    /// Reads a data file into the registry's element, for the server, and into the NBT clients are sent.
    /// </summary>
    /// <param name="registry">One of <see cref="Registries"/>.</param>
    /// <param name="identifier">The entry's identifier, like <c>ruby:crystal_caves</c>.</param>
    /// <param name="source">Where the file comes from, for messages.</param>
    /// <param name="isPlacedFeature">Whether a placed feature a biome refers to exists; vanilla's by default.</param>
    /// <param name="isConfiguredCarver">
    /// Whether a configured carver a biome refers to exists; vanilla's by default.
    /// </param>
    /// <exception cref="InvalidDataException">The file isn't valid, saying where and what's expected.</exception>
    public static CodecFile Parse(
        string registry,
        string identifier,
        string source,
        ReadOnlySpan<byte> json,
        Func<string, bool>? isPlacedFeature = null,
        Func<string, bool>? isConfiguredCarver = null)
    {
        if (!PluginTags.IsFileIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        try
        {
            var bytes = json.ToArray();
            using var document = JsonDocument.Parse(bytes, documentOptions);
            var root = document.RootElement;

            object element;
            NbtCompound data;

            switch (registry)
            {
                case Biome:
                    var biome = BiomeData.Read(root, isPlacedFeature, isConfiguredCarver);
                    element = biome.ToElement();
                    data = biome.ToNetworkNbt();
                    break;
                case DimensionType:
                    var dimension = DimensionTypeData.Read(root);
                    element = dimension.ToElement();
                    data = dimension.ToNetworkNbt();
                    break;
                case DamageType:
                    var damageType = DamageTypeData.Read(root);
                    element = damageType;
                    data = DamageTypeData.ToNetworkNbt(damageType);
                    break;
                default:
                    throw new ArgumentException($"{registry} isn't read from plugins' files.", nameof(registry));
            }

            return new CodecFile(registry, identifier, source, element, Encoding.UTF8.GetString(bytes), data);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"{source} isn't a valid {registry} file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Adds the files' entries to their registries, or gives the entries they added before their new data. Plugins'
    /// entries no file has any more stay as they were; vanilla's entries no file overrides any more are vanilla's
    /// again.
    /// </summary>
    /// <param name="files">The loaded plugins' files in load order, then data packs' files in theirs.</param>
    /// <param name="levelsCreated">Whether levels exist, sized by vanilla's dimension types, which can't change height
    /// then. A plugin's dimension type can't once added either way.</param>
    public static void Apply(IEnumerable<CodecFile> files, ILogger logger, bool levelsCreated = true)
    {
        lock (applying)
        {
            var kept = Winners(files, logger);
            Volatile.Write(ref applied, kept.ToArray());

            var changed = CodecRegistry.Biomes.Table.Apply(
                Entries<BiomeElement>(kept, Biome),
                (name, id, element) => new BiomeCodec { Name = name, Id = id, Element = element },
                codec => codec.Element,
                (codec, element) => codec.Element = element);

            changed |= CodecRegistry.Dimensions.Table.Apply(
                Entries<DimensionElement>(kept, DimensionType),
                (name, id, element) => new DimensionCodec { Name = name, Id = id, Element = element },
                codec => codec.Element,
                (codec, element) => codec.Element = element,
                (codec, element) => KeepsHeight(codec, element, levelsCreated, logger));

            changed |= CodecRegistry.DamageType.Table.Apply(
                Entries<DamageTypeElement>(kept, DamageType),
                (name, id, element) => new DamageTypeCodec { Name = name, Id = id, Element = element },
                codec => codec.Element,
                (codec, element) => codec.Element = element);

            if (changed)
                Interlocked.Increment(ref revision);
        }
    }

    /// <summary>
    /// The files whose entries apply: the last in order for each entry (see <see cref="VanillaOverrides.LastWins"/>).
    /// </summary>
    public static List<CodecFile> Winners(IEnumerable<CodecFile> files, ILogger logger) =>
        VanillaOverrides.LastWins(files, file => (file.Registry, file.Identifier), file => file.Source, logger);

    // A dimension type in use keeps its height until the server stops: levels and clients' chunks are sized by it when
    // created, so a change would leave them disagreeing. Vanilla's are in use once levels exist, plugins' once added.
    private static bool KeepsHeight(DimensionCodec codec, DimensionElement element, bool levelsCreated, ILogger logger)
    {
        if (codec.Id < CodecRegistry.Dimensions.Table.VanillaCount && !levelsCreated)
            return true;

        var current = codec.Element;
        if (current.MinY == element.MinY
            && current.Height == element.Height
            && current.LogicalHeight == element.LogicalHeight)
        {
            return true;
        }

        Log.HeightChanged(logger, codec.Name);
        return false;
    }

    // A registry's entries of the files, in their order.
    private static IEnumerable<PluginCodecEntry<TElement>> Entries<TElement>(List<CodecFile> files, string registry) =>
        files
            .Where(file => file.Registry == registry)
            .Select(file => new PluginCodecEntry<TElement>(
                file.Identifier,
                (TElement)file.Element,
                file.Json,
                file.Data));

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Kept {Identifier} as it was: its height can't change until the server restarts")]
        public static partial void HeightChanged(ILogger logger, string identifier);
    }
}

/// <summary>
/// A plugin's or data pack's biome, dimension type or damage type file.
/// </summary>
/// <param name="Registry">The entry's registry, one of <see cref="PluginCodecs.Registries"/>.</param>
/// <param name="Identifier">The entry's identifier, like <c>ruby:crystal_caves</c>.</param>
/// <param name="Source">Where the file comes from, for messages.</param>
/// <param name="Element">The file read as the registry's element type.</param>
/// <param name="Json">The file's text.</param>
/// <param name="Data">The entry as vanilla's network codec writes it, which clients are sent.</param>
internal sealed record CodecFile(
    string Registry,
    string Identifier,
    string Source,
    object Element,
    string Json,
    NbtCompound Data);
