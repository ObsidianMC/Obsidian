using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// Typed reads of 26.3 worldgen JSON values with the ranges vanilla's codecs check, reporting where a bad value is.
/// </summary>
internal static class DensityJson
{
    public static InvalidDataException Error(string path, string message) =>
        new(path.Length == 0 ? message : $"{path}: {message}");

    public static string Field(string path, string field) => path.Length == 0 ? field : $"{path}.{field}";

    public static string Index(string path, int index) => $"{path}[{index}]";

    public static JsonElement Object(JsonElement json, string path) =>
        json.ValueKind == JsonValueKind.Object ? json : throw Error(path, "expected an object");

    public static JsonElement.ArrayEnumerator Array(JsonElement json, string path) =>
        json.ValueKind == JsonValueKind.Array ? json.EnumerateArray() : throw Error(path, "expected a list");

    public static JsonElement Required(JsonElement json, string path, string field)
    {
        Object(json, path);

        return json.TryGetProperty(field, out var value) ? value : throw Error(path, $"missing {field}");
    }

    public static string String(JsonElement json, string path) =>
        json.ValueKind == JsonValueKind.String ? json.GetString()! : throw Error(path, "expected a string");

    public static bool Boolean(JsonElement json, string path) => json.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Error(path, "expected true or false")
    };

    public static int Int(JsonElement json, string path, int min = int.MinValue, int max = int.MaxValue)
    {
        if (json.ValueKind != JsonValueKind.Number || !json.TryGetInt32(out var value))
            throw Error(path, "expected an integer");

        return value >= min && value <= max ? value : throw Error(path, $"{value} isn't from {min} to {max}");
    }

    public static double Double(JsonElement json, string path, double min = double.MinValue,
        double max = double.MaxValue)
    {
        if (json.ValueKind != JsonValueKind.Number)
            throw Error(path, "expected a number");

        var value = json.GetDouble();
        return value >= min && value <= max ? value : throw Error(path, OutOfRange(value, min, max));
    }

    /// <summary>
    /// A float, parsed straight from the number's text like Java's Float.parseFloat (narrowing a parsed double can
    /// round twice).
    /// </summary>
    public static float Float(JsonElement json, string path, float min = float.MinValue, float max = float.MaxValue)
    {
        if (json.ValueKind != JsonValueKind.Number)
            throw Error(path, "expected a number");

        var value = float.Parse(json.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return value >= min && value <= max ? value : throw Error(path, OutOfRange(value, min, max));
    }

    private static string OutOfRange(double value, double min, double max) =>
        string.Create(CultureInfo.InvariantCulture, $"{value} isn't from {min} to {max}");
}
