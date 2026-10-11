using Microsoft.Extensions.Logging;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.DamageTypes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Nbt;
using System.Collections.Frozen;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// overridden, and when plugins define the same entry, the first in load order wins. Clients are sent plugins' entries
/// with their files as NBT, and vanilla's by name.
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

    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // Lenient, like vanilla reading data packs with its codecs: unknown fields are ignored and numbers may be strings.
    private static readonly JsonSerializerOptions elementOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
            new LightLevelConverter(),
            new LenientStringConverter()
        }
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
    /// Reads a data file into the registry's element, for the server, and into NBT, for clients.
    /// </summary>
    /// <param name="registry">One of <see cref="Registries"/>.</param>
    /// <param name="identifier">The entry's identifier, like <c>ruby:crystal_caves</c>.</param>
    /// <param name="source">Where the file comes from, for messages.</param>
    /// <exception cref="InvalidDataException">
    /// The file isn't valid, or it's for an entry in the <c>minecraft</c> namespace, which plugins can't override.
    /// </exception>
    public static CodecFile Parse(string registry, string identifier, string source, ReadOnlySpan<byte> json)
    {
        if (!PluginTags.IsIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        // Clients take vanilla's entries from the minecraft:core pack they share with the server.
        if (identifier.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new InvalidDataException($"{source} would override vanilla's {identifier}, which plugins can't.");

        try
        {
            var bytes = json.ToArray();
            using var document = JsonDocument.Parse(bytes, documentOptions);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{source} isn't a JSON object.");

            object? element = registry switch
            {
                Biome => root.Deserialize<BiomeElement>(elementOptions),
                DimensionType => root.Deserialize<DimensionElement>(elementOptions),
                DamageType => root.Deserialize<DamageTypeElement>(elementOptions),
                _ => throw new ArgumentException($"{registry} isn't read from plugins' files.", nameof(registry))
            };

            if (element is null)
                throw new InvalidDataException($"{source} is empty.");

            var data = (NbtCompound)ToNbt(root)!;

            return new CodecFile(registry, identifier, source, element, Encoding.UTF8.GetString(bytes), data);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException
            or FormatException)
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
                Entries<DimensionElement>(kept, DimensionType),
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

    // A registry's entries of the files, in their order.
    private static IEnumerable<PluginCodecEntry<TElement>> Entries<TElement>(List<CodecFile> files, string registry) =>
        files
            .Where(file => file.Registry == registry)
            .Select(file => new PluginCodecEntry<TElement>(
                file.Identifier,
                (TElement)file.Element,
                file.Json,
                file.Data));

    // JSON as the NBT clients are sent, the way vanilla converts between them (JsonOps to NbtOps): objects become
    // compounds, arrays lists, strings string tags and booleans byte tags (1 or 0), and every number a double tag, as
    // NbtOps.createNumeric makes them; the client's codecs read any numeric tag as the number they expect. Nulls are
    // left out, and, like Gson, a key repeated in an object keeps its last value.
    private static INbtTag? ToNbt(JsonElement element, string name = "")
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var compound = new NbtCompound(name);

                foreach (var property in element.EnumerateObject())
                {
                    // Set rather than added: vanilla allows empty and repeated keys.
                    if (ToNbt(property.Value, property.Name) is INbtTag child)
                        compound.SetReadTag(property.Name, child);
                }

                return compound;
            case JsonValueKind.Array:
                return ToList(element, name);
            case JsonValueKind.String:
                return new NbtTag<string>(name, element.GetString());
            case JsonValueKind.True:
                return new NbtTag<byte>(name, 1);
            case JsonValueKind.False:
                return new NbtTag<byte>(name, 0);
            case JsonValueKind.Number:
                return new NbtTag<double>(name, element.GetDouble());
            default:
                return null;
        }
    }

    // A list holds one type of tag. A list of mixed types is one of compounds, like vanilla's ListTag makes it: each
    // element that isn't a compound, or is a compound that looks like a wrapper, is wrapped in a compound under an
    // empty key, which clients unwrap.
    private static NbtList ToList(JsonElement array, string name)
    {
        var tags = array.EnumerateArray()
            .Select(item => ToNbt(item))
            .OfType<INbtTag>()
            .ToList();

        var types = tags.Select(tag => tag.Type).Distinct().ToList();
        var mixed = types.Count > 1;
        var listType = types.Count switch
        {
            0 => NbtTagType.End,
            1 => types[0],
            _ => NbtTagType.Compound
        };

        var list = new NbtList(listType, name);

        foreach (var tag in tags)
        {
            var isWrapper = tag is NbtCompound compound && compound.Count == 1 && compound.HasTag(string.Empty);

            if (mixed && (tag is not NbtCompound || isWrapper))
            {
                var wrapper = new NbtCompound();
                wrapper.SetReadTag(string.Empty, tag);
                list.Add(wrapper);
            }
            else
            {
                list.Add(tag);
            }
        }

        return list;
    }

    // Vanilla's monster_spawn_light_level is an int provider: a number, or an object like
    // {"type": "minecraft:uniform", "min_inclusive": 0, "max_inclusive": 7}.
    private sealed class LightLevelConverter : JsonConverter<MonsterSpawnLightLevel>
    {
        public override MonsterSpawnLightLevel Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
                return new MonsterSpawnLightLevel { IntValue = reader.GetInt32() };

            return new MonsterSpawnLightLevel
            {
                Value = JsonSerializer.Deserialize<MonsterSpawnLightLevelValue>(ref reader, options)
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            MonsterSpawnLightLevel value,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }

    // Reads any value into a string member, a value that isn't a string as its JSON text: vanilla accepts some values
    // in several forms (a colour as a number or a string, a sound as an id or an object), which the elements' string
    // members only hold as text.
    private sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return reader.GetString();

            using var value = JsonDocument.ParseValue(ref reader);
            return value.RootElement.GetRawText();
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Left out {Identifier} from {Source}: {First} defined it first")]
        public static partial void Duplicate(ILogger logger, string identifier, string source, string first);
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
/// <param name="Data">The file as NBT, which clients are sent.</param>
internal sealed record CodecFile(
    string Registry,
    string Identifier,
    string Source,
    object Element,
    string Json,
    NbtCompound Data);
