using Obsidian.Nbt;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Generators.Mojang;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>
/// Compares a level stem a vanilla level.dat saved (in <c>WorldGenSettings.dimensions</c>) with a level stem file, to
/// tell whether a world made in vanilla with a data pack generates the same with the data pack's file.
/// </summary>
/// <remarks>
/// Vanilla's codecs write the stem's values into NBT with their own types, so the comparison allows for how they differ
/// from a file's JSON: numbers are compared at the precision saved (a float holds a file's <c>0.1</c> as a float),
/// booleans are bytes and an identifier may leave out <c>minecraft:</c>. The biome source is compared as vanilla's
/// codecs read it: climate parameters and offsets quantized, in any of their forms (a number, <c>[min, max]</c> or
/// <c>{"min": ..., "max": ...}</c>), a checkerboard's <c>scale</c> defaulting to 2 and its biomes a single biome or a
/// list. Everything else must be the same, fields included.
/// </remarks>
internal static class SavedLevelStem
{
    private const string BiomeSourcePath = "generator.biome_source";

    private static readonly string[] climateParameters =
        ["temperature", "humidity", "continentalness", "erosion", "depth", "weirdness"];

    /// <summary>
    /// Compares the field <paramref name="name"/> of a saved object with a file's, either of them possibly absent.
    /// </summary>
    /// <returns>The path of the first difference, or null when they're the same.</returns>
    private delegate string? FieldComparison(string name, INbtTag? saved, JsonElement? file, string path);

    /// <summary>
    /// Finds where a saved stem differs from a file's.
    /// </summary>
    /// <returns>The path of the first difference, like <c>generator.biome_source.biomes[3].biome</c>, or null when
    /// they're the same.</returns>
    public static string? FindDifference(INbtTag saved, JsonElement file) => Difference(saved, file, "");

    private static string? Difference(INbtTag saved, JsonElement file, string path)
    {
        switch (file.ValueKind)
        {
            case JsonValueKind.Object when saved is NbtCompound compound:
            {
                if (path != BiomeSourcePath)
                    return FieldsDifference(compound, file, path, SameField);

                var type = file.TryGetProperty("type", out var value) && value.ValueKind == JsonValueKind.String
                    ? Identifier(value.GetString()!)
                    : null;

                return FieldsDifference(
                    compound,
                    file,
                    path,
                    (name, savedField, fileField, fieldPath) =>
                        BiomeSourceField(type, name, savedField, fileField, fieldPath));
            }
            case JsonValueKind.Array when Elements(saved) is IReadOnlyList<INbtTag> elements:
            {
                if (elements.Count != file.GetArrayLength())
                    return path;

                for (var i = 0; i < elements.Count; i++)
                {
                    if (Difference(elements[i], file[i], $"{path}[{i}]") is string difference)
                        return difference;
                }

                return null;
            }
            case JsonValueKind.Number:
                return NumberEquals(saved, file) ? null : path;
            case JsonValueKind.True or JsonValueKind.False:
                return saved is NbtTag<byte> flag && flag.Value == (file.ValueKind == JsonValueKind.True ? 1 : 0)
                    ? null
                    : path;
            case JsonValueKind.String:
                return saved is NbtTag<string> text && Identifier(text.Value ?? "") == Identifier(file.GetString()!)
                    ? null
                    : path;
            default:
                return path;
        }
    }

    // Compares the fields both sides have, and those only one has, with compare.
    private static string? FieldsDifference(
        NbtCompound saved,
        JsonElement file,
        string path,
        FieldComparison compare)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in file.EnumerateObject())
        {
            names.Add(property.Name);

            var value = saved.TryGetTag(property.Name, out var tag) ? tag : null;
            if (compare(property.Name, value, property.Value, Join(path, property.Name)) is string difference)
                return difference;
        }

        foreach (var (name, tag) in saved)
        {
            if (!names.Contains(name) && compare(name, tag, null, Join(path, name)) is string difference)
                return difference;
        }

        return null;
    }

    // A field without a default: both sides must have it, the same.
    private static string? SameField(string name, INbtTag? saved, JsonElement? file, string path) =>
        saved is not null && file is JsonElement value ? Difference(saved, value, path) : path;

    // Vanilla's biome source codecs: a checkerboard's scale defaults to 2 and its biomes may be a single biome, and
    // a multi-noise source's inline biomes have climate parameters in several forms.
    private static string? BiomeSourceField(
        string? type,
        string name,
        INbtTag? saved,
        JsonElement? file,
        string path) =>
        (type, name) switch
        {
            ("minecraft:checkerboard", "scale") => CheckerboardScale(saved) == CheckerboardScale(file) ? null : path,
            ("minecraft:checkerboard", "biomes") => BiomesDifference(saved, file, path),
            ("minecraft:multi_noise", "biomes") => ParameterListDifference(saved, file, path),
            _ => SameField(name, saved, file, path)
        };

    private static int CheckerboardScale(INbtTag? saved) =>
        LevelStemData.Scale(saved is null ? null : ToJson(saved));

    private static int CheckerboardScale(JsonElement? file) => LevelStemData.Scale(file);

    // A biome holder set: one biome, a list of them or a tag. Vanilla saves a list of one biome as the biome.
    private static string? BiomesDifference(INbtTag? saved, JsonElement? file, string path)
    {
        if (saved is null || file is not JsonElement value)
            return path;

        var savedBiomes = Biomes(ToJson(saved));
        var fileBiomes = Biomes(value);

        return savedBiomes is not null && fileBiomes is not null && savedBiomes.SequenceEqual(fileBiomes)
            ? null
            : path;

        static List<string>? Biomes(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => [Identifier(value.GetString()!)],
            JsonValueKind.Array when value.EnumerateArray().All(biome => biome.ValueKind == JsonValueKind.String) =>
                [.. value.EnumerateArray().Select(biome => Identifier(biome.GetString()!))],
            _ => null
        };
    }

    // A multi-noise source's inline biomes: {"biome": ..., "parameters": ...} entries.
    private static string? ParameterListDifference(INbtTag? saved, JsonElement? file, string path)
    {
        if (saved is null
            || Elements(saved) is not IReadOnlyList<INbtTag> entries
            || file is not JsonElement { ValueKind: JsonValueKind.Array } list
            || entries.Count != list.GetArrayLength())
        {
            return path;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entryPath = $"{path}[{i}]";
            if (entries[i] is not NbtCompound entry || list[i].ValueKind != JsonValueKind.Object)
                return entryPath;

            if (FieldsDifference(entry, list[i], entryPath, EntryField) is string difference)
                return difference;
        }

        return null;

        static string? EntryField(string name, INbtTag? saved, JsonElement? file, string path) =>
            name == "parameters" ? ParameterPointDifference(saved, file, path) : SameField(name, saved, file, path);
    }

    private static string? ParameterPointDifference(INbtTag? saved, JsonElement? file, string path)
    {
        if (saved is not NbtCompound point || file is not JsonElement { ValueKind: JsonValueKind.Object } value)
            return path;

        return FieldsDifference(point, value, path, PointField);

        static string? PointField(string name, INbtTag? saved, JsonElement? file, string path)
        {
            if (name == "offset")
                return SameQuantized(saved, file, path, Offset);

            return climateParameters.Contains(name)
                ? SameQuantized(saved, file, path, LevelStemData.Parameter)
                : SameField(name, saved, file, path);
        }

        static long Offset(JsonElement value, string path) => Climate.Quantize(CodecJson.Float(value, path, 0f, 1f));
    }

    // Vanilla keeps climate values quantized, so values in other forms, or that only differ below the quantization's
    // precision, generate the same.
    private static string? SameQuantized<T>(
        INbtTag? saved,
        JsonElement? file,
        string path,
        Func<JsonElement, string, T> read)
    {
        if (saved is null || file is not JsonElement value)
            return path;

        try
        {
            return EqualityComparer<T>.Default.Equals(read(ToJson(saved), path), read(value, path)) ? null : path;
        }
        catch (InvalidDataException)
        {
            return path;
        }
    }

    private static IReadOnlyList<INbtTag>? Elements(INbtTag tag) => tag switch
    {
        NbtList list => [.. list],
        NbtArray<int> ints => [.. ints.GetArray().Select(value => new NbtTag<int>("", value))],
        NbtArray<long> longs => [.. longs.GetArray().Select(value => new NbtTag<long>("", value))],
        NbtArray<byte> bytes => [.. bytes.GetArray().Select(value => new NbtTag<byte>("", value))],
        _ => null
    };

    // At the saved value's precision: a float compares with the file's number read as a float.
    private static bool NumberEquals(INbtTag saved, JsonElement file) => saved switch
    {
        NbtTag<float> value => file.TryGetSingle(out var number) && number == value.Value,
        NbtTag<double> value => file.TryGetDouble(out var number) && number == value.Value,
        NbtTag<byte> value => file.TryGetInt64(out var number) && number == value.Value,
        NbtTag<short> value => file.TryGetInt64(out var number) && number == value.Value,
        NbtTag<int> value => file.TryGetInt64(out var number) && number == value.Value,
        NbtTag<long> value => file.TryGetInt64(out var number) && number == value.Value,
        _ => false
    };

    /// <summary>
    /// Writes a saved value as JSON, for reading it like a file's: numbers keep their saved precision.
    /// </summary>
    private static JsonElement ToJson(INbtTag tag)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            Write(writer, tag);

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();

        static void Write(Utf8JsonWriter writer, INbtTag tag)
        {
            switch (tag)
            {
                case NbtCompound compound:
                    writer.WriteStartObject();

                    foreach (var (name, child) in compound)
                    {
                        writer.WritePropertyName(name);
                        Write(writer, child);
                    }

                    writer.WriteEndObject();
                    break;
                case NbtTag<string> text:
                    writer.WriteStringValue(text.Value);
                    break;
                case NbtTag<float> value:
                    writer.WriteNumberValue(value.Value);
                    break;
                case NbtTag<double> value:
                    writer.WriteNumberValue(value.Value);
                    break;
                case NbtTag<byte> value:
                    writer.WriteNumberValue(value.Value);
                    break;
                case NbtTag<short> value:
                    writer.WriteNumberValue(value.Value);
                    break;
                case NbtTag<int> value:
                    writer.WriteNumberValue(value.Value);
                    break;
                case NbtTag<long> value:
                    writer.WriteNumberValue(value.Value);
                    break;
                default:
                    writer.WriteStartArray();

                    foreach (var element in Elements(tag) ?? [])
                        Write(writer, element);

                    writer.WriteEndArray();
                    break;
            }
        }
    }

    // Defaults the namespace to minecraft, also for a tag's (#name).
    private static string Identifier(string value)
    {
        if (value.StartsWith('#'))
            return $"#{Identifier(value[1..])}";

        return value.Contains(':') ? value : $"minecraft:{value}";
    }

    private static string Join(string path, string name) =>
        path.Length == 0 ? name : string.Create(CultureInfo.InvariantCulture, $"{path}.{name}");
}
