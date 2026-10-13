using Obsidian.Nbt;
using System.Globalization;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>
/// Compares a level stem a vanilla level.dat saved (in <c>WorldGenSettings.dimensions</c>) with a level stem file, to
/// tell whether a world made in vanilla with a data pack generates the same with the data pack's file.
/// </summary>
/// <remarks>
/// Vanilla's codecs write the stem's values into NBT with their own types, so the comparison allows for how they differ
/// from a file's JSON: numbers are compared at the precision saved (a float holds a file's <c>0.1</c> as a float),
/// booleans are bytes, an identifier may leave out <c>minecraft:</c>, and a climate parameter is either one number or a
/// <c>[min, max]</c> pair of equal numbers. Everything else must be the same, fields included.
/// </remarks>
internal static class SavedLevelStem
{
    /// <summary>
    /// Finds where a saved stem differs from a file's.
    /// </summary>
    /// <returns>The path of the first difference, like <c>generator.biome_source.biomes[3].biome</c>, or null when
    /// they're the same.</returns>
    public static string? FindDifference(INbtTag saved, JsonElement file) => Difference(saved, file, "");

    private static string? Difference(INbtTag saved, JsonElement file, string path)
    {
        if (IsPair(saved, file) is bool pair)
            return pair ? null : path;

        switch (file.ValueKind)
        {
            case JsonValueKind.Object when saved is NbtCompound compound:
            {
                var names = file.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
                if (compound.Select(child => child.Key).FirstOrDefault(name => !names.Contains(name)) is string extra)
                    return Join(path, extra);

                foreach (var property in file.EnumerateObject())
                {
                    var child = Join(path, property.Name);
                    if (!compound.TryGetTag(property.Name, out var value))
                        return child;

                    if (Difference(value, property.Value, child) is string difference)
                        return difference;
                }

                return null;
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

    // A climate parameter: one number on one side, a [min, max] pair of the same number on the other. Null when the
    // values aren't of these shapes.
    private static bool? IsPair(INbtTag saved, JsonElement file)
    {
        if (file.ValueKind == JsonValueKind.Number
            && Elements(saved) is [var min, var max])
        {
            return NumberEquals(min, file) && NumberEquals(max, file);
        }

        if (file.ValueKind == JsonValueKind.Array
            && file.GetArrayLength() == 2
            && file[0].ValueKind == JsonValueKind.Number
            && file[1].ValueKind == JsonValueKind.Number
            && IsNumber(saved))
        {
            return NumberEquals(saved, file[0]) && NumberEquals(saved, file[1]);
        }

        return null;
    }

    private static IReadOnlyList<INbtTag>? Elements(INbtTag tag) => tag switch
    {
        NbtList list => [.. list],
        NbtArray<int> ints => [.. ints.GetArray().Select(value => new NbtTag<int>("", value))],
        NbtArray<long> longs => [.. longs.GetArray().Select(value => new NbtTag<long>("", value))],
        NbtArray<byte> bytes => [.. bytes.GetArray().Select(value => new NbtTag<byte>("", value))],
        _ => null
    };

    private static bool IsNumber(INbtTag tag) =>
        tag is NbtTag<byte> or NbtTag<short> or NbtTag<int> or NbtTag<long> or NbtTag<float> or NbtTag<double>;

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

    private static string Identifier(string value) => value.Contains(':') ? value : $"minecraft:{value}";

    private static string Join(string path, string name) =>
        path.Length == 0 ? name : string.Create(CultureInfo.InvariantCulture, $"{path}.{name}");
}
