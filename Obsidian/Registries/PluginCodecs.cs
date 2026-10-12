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
/// Adds plugins' biomes, dimension types and damage types from their data files, like a data pack's: a file
/// <c>data/&lt;namespace&gt;/worldgen/biome/&lt;path&gt;.json</c> (or <c>dimension_type</c>, <c>damage_type</c>)
/// is the entry <c>&lt;namespace&gt;:&lt;path&gt;</c>.
/// </summary>
/// <remarks>
/// Entries are added to the codec registries after vanilla's (see <see cref="CodecTable{TCodec}"/>) and stay until the
/// server stops, even once their plugin unloads, since chunks and saves refer to them. Vanilla's entries can't be
/// overridden, and when plugins define the same entry, the first in load order wins.
/// <para>
/// A file is read the way vanilla 1.21.11 reads a data pack's entry with the registry's codec, and rejected where
/// vanilla would reject it (see <see cref="BiomeData"/>, <see cref="DimensionTypeData"/> and
/// <see cref="DamageTypeData"/>). Clients are sent plugins' entries as vanilla's network codecs write the read values,
/// so they always decode them, and vanilla's entries by name.
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
    /// <exception cref="InvalidDataException">
    /// The file isn't valid, saying where and what's expected, or it's for an entry in the <c>minecraft</c>
    /// namespace, which plugins can't override.
    /// </exception>
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

        // Clients take vanilla's entries from the minecraft:core pack they share with the server.
        if (identifier.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new InvalidDataException($"{source} would override vanilla's {identifier}, which plugins can't.");

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
    /// Adds the files' entries to their registries, or gives the entries they added before their new data. Entries
    /// no file has any more stay as they were.
    /// </summary>
    /// <param name="files">The loaded plugins' files, in load order.</param>
    public static void Apply(IEnumerable<CodecFile> files, ILogger logger)
    {
        lock (applying)
        {
            // The first plugin in load order defining an entry wins.
            var kept = new List<CodecFile>();
            var byEntry = new Dictionary<(string Registry, string Identifier), CodecFile>();

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

            var changed = CodecRegistry.Biomes.Table.Apply(
                Entries<BiomeElement>(kept, Biome),
                (name, id, element) => new BiomeCodec { Name = name, Id = id, Element = element },
                (codec, element) => codec.Element = element);

            changed |= CodecRegistry.Dimensions.Table.Apply(
                Entries<DimensionElement>(kept, DimensionType).Where(entry => KeepsHeight(entry, logger)),
                (name, id, element) => new DimensionCodec { Name = name, Id = id, Element = element },
                (codec, element) => codec.Element = element);

            changed |= CodecRegistry.DamageType.Table.Apply(
                Entries<DamageTypeElement>(kept, DamageType),
                (name, id, element) => new DamageTypeCodec { Name = name, Id = id, Element = element },
                (codec, element) => codec.Element = element);

            if (changed)
                Interlocked.Increment(ref revision);
        }
    }

    // A plugin's dimension type in use keeps its height until the server stops: levels and clients' chunks are sized
    // by it when created, so a reload changing it would leave them disagreeing.
    private static bool KeepsHeight(PluginCodecEntry<DimensionElement> entry, ILogger logger)
    {
        if (!CodecRegistry.Dimensions.All.TryGetValue(entry.Name, out var existing))
            return true;

        var current = existing.Element;
        if (current.MinY == entry.Element.MinY
            && current.Height == entry.Element.Height
            && current.LogicalHeight == entry.Element.LogicalHeight)
        {
            return true;
        }

        Log.HeightChanged(logger, entry.Name);
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
            Message = "Left out {Identifier} from {Source}: {First} defined it first")]
        public static partial void Duplicate(ILogger logger, string identifier, string source, string first);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Kept {Identifier} as it was: its height can't change until the server restarts")]
        public static partial void HeightChanged(ILogger logger, string identifier);
    }
}

/// <summary>
/// A plugin's biome, dimension type or damage type file.
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
