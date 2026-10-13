using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// Reads values of plugins' data files the way vanilla's codecs read JSON (DataFixerUpper's uncompressed JsonOps), so
/// a file is accepted exactly when vanilla would accept it. A value that doesn't fit throws an
/// <see cref="InvalidDataException"/> naming its path, like <c>effects.water_color</c>, and what's expected.
/// </summary>
/// <remarks>
/// As JsonOps: booleans, numbers and strings must be JSON values of that kind (a number in a string isn't one), a JSON
/// null is an absent field, and an int field truncates a fractional number. A repeated key keeps its last value, as
/// Gson does, and fields a codec doesn't know are ignored.
/// </remarks>
internal static class CodecJson
{
    public static InvalidDataException Error(string path, string message) =>
        new(path.Length == 0 ? message : $"{path}: {message}");

    public static string Field(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    public static string Index(string path, int index) => $"{path}[{index}]";

    public static JsonElement Object(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Object ? value : throw Error(path, "expected an object");

    public static JsonElement Array(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Array ? value : throw Error(path, "expected a list");

    /// <summary>
    /// Gets a field of an object; a null field counts as absent.
    /// </summary>
    public static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
            return true;

        value = default;
        return false;
    }

    public static JsonElement Required(JsonElement obj, string path, string name) =>
        TryGet(obj, name, out var value) ? value : throw Error(Field(path, name), "missing");

    public static bool Bool(JsonElement value, string path) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Error(path, "expected true or false")
    };

    // Like Java's Number.intValue of the number Gson parsed: a fraction is truncated, a long keeps its low 32 bits.
    public static int Int(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw Error(path, "expected an int");

        if (value.TryGetInt32(out var result))
            return result;

        if (value.TryGetInt64(out var wide))
            return unchecked((int)wide);

        return unchecked((int)(long)Math.Truncate(ParseDouble(value)));
    }

    public static int Int(JsonElement value, string path, int min, int max)
    {
        var result = Int(value, path);
        if (result < min || result > max)
            throw Error(path, $"{result} isn't from {min} to {max}");

        return result;
    }

    public static float Float(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw Error(path, "expected a number");

        return float.Parse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public static float Float(JsonElement value, string path, float min, float max)
    {
        var result = Float(value, path);
        if (!(result >= min && result <= max))
            throw Error(path, $"{Format(result)} isn't from {Format(min)} to {Format(max)}");

        return result;
    }

    public static double Double(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw Error(path, "expected a number");

        return ParseDouble(value);
    }

    public static double Double(JsonElement value, string path, double min, double max)
    {
        var result = Double(value, path);
        if (!(result >= min && result <= max))
            throw Error(path, $"{Format(result)} isn't from {Format(min)} to {Format(max)}");

        return result;
    }

    public static string String(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Error(path, "expected a string");

    /// <summary>
    /// Reads a string that is one of an enum's serialized names, like vanilla's StringRepresentable codecs.
    /// </summary>
    public static string Enum(JsonElement value, string path, params ReadOnlySpan<string> names)
    {
        var name = String(value, path);
        foreach (var allowed in names)
        {
            if (name == allowed)
                return name;
        }

        throw Error(path, $"\"{name}\" isn't one of {string.Join(", ", names.ToArray())}");
    }

    /// <summary>
    /// Reads an identifier like vanilla's Identifier codec, which defaults the namespace to <c>minecraft</c>.
    /// </summary>
    /// <returns>The identifier with its namespace, like <c>minecraft:plains</c>.</returns>
    public static string Identifier(JsonElement value, string path) =>
        TryParseIdentifier(String(value, path), out var identifier)
            ? identifier
            : throw Error(path, $"\"{value.GetString()}\" isn't a valid identifier");

    /// <summary>
    /// Parses an identifier like vanilla's Identifier.parse: the namespace (before a colon) defaults to
    /// <c>minecraft</c> and takes <c>[a-z0-9_.-]</c>, and the path also takes <c>/</c>.
    /// </summary>
    public static bool TryParseIdentifier(string text, out string identifier)
    {
        var colon = text.IndexOf(':');
        var space = colon > 0 ? text[..colon] : "minecraft";
        var path = colon >= 0 ? text[(colon + 1)..] : text;

        identifier = $"{space}:{path}";
        return space.All(IsNamespaceChar) && path.All(c => IsNamespaceChar(c) || c == '/');

        static bool IsNamespaceChar(char c) =>
            c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.';
    }

    /// <summary>
    /// Reads a tag reference like <c>#minecraft:infiniburn_overworld</c>, as vanilla's TagKey.hashedCodec.
    /// </summary>
    /// <returns>The tag's identifier, without the <c>#</c>.</returns>
    public static string Tag(JsonElement value, string path)
    {
        var text = String(value, path);
        if (!text.StartsWith('#'))
            throw Error(path, $"\"{text}\" isn't a tag: it has to start with #");

        return TryParseIdentifier(text[1..], out var identifier)
            ? identifier
            : throw Error(path, $"\"{text}\" isn't a valid tag identifier");
    }

    /// <summary>
    /// Reads an RGB colour like vanilla's ExtraCodecs.STRING_RGB_COLOR: <c>"#rrggbb"</c> (made opaque), an int as
    /// is, or a list of 3 floats from 0 to 1.
    /// </summary>
    public static int RgbColor(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.String)
            return unchecked((int)(HexColor(value, path, digits: 6) | 0xFF000000));

        return value.ValueKind == JsonValueKind.Array
            ? FloatColor(value, path, channels: 3)
            : value.ValueKind == JsonValueKind.Number
                ? Int(value, path)
                : throw Error(path, "expected a colour: \"#rrggbb\", an int or a list of 3 floats");
    }

    /// <summary>
    /// Reads an ARGB colour like vanilla's ExtraCodecs.STRING_ARGB_COLOR: <c>"#aarrggbb"</c>, an int, or a list of
    /// 4 floats (red, green, blue, alpha) from 0 to 1.
    /// </summary>
    public static int ArgbColor(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.String)
            return unchecked((int)HexColor(value, path, digits: 8));

        return value.ValueKind == JsonValueKind.Array
            ? FloatColor(value, path, channels: 4)
            : value.ValueKind == JsonValueKind.Number
                ? Int(value, path)
                : throw Error(path, "expected a colour: \"#aarrggbb\", an int or a list of 4 floats");
    }

    /// <summary>
    /// Reads an RGB colour like vanilla's ExtraCodecs.RGB_COLOR_CODEC: an int, or a list of 3 floats.
    /// </summary>
    public static int RgbIntColor(JsonElement value, string path) => value.ValueKind switch
    {
        JsonValueKind.Number => Int(value, path),
        JsonValueKind.Array => FloatColor(value, path, channels: 3),
        _ => throw Error(path, "expected a colour: an int or a list of 3 floats")
    };

    /// <summary>
    /// Reads an ARGB colour like vanilla's ExtraCodecs.ARGB_COLOR_CODEC: an int, or a list of 4 floats (red, green,
    /// blue, alpha).
    /// </summary>
    public static int ArgbIntColor(JsonElement value, string path) => value.ValueKind switch
    {
        JsonValueKind.Number => Int(value, path),
        JsonValueKind.Array => FloatColor(value, path, channels: 4),
        _ => throw Error(path, "expected a colour: an int or a list of 4 floats")
    };

    /// <summary>
    /// An RGB colour as vanilla's STRING_RGB_COLOR writes it: <c>#rrggbb</c> in lower case, without alpha.
    /// </summary>
    public static string FormatRgb(int color) => $"#{color & 0xFFFFFF:x6}";

    /// <summary>
    /// An ARGB colour as vanilla's STRING_ARGB_COLOR writes it: <c>#aarrggbb</c> in lower case.
    /// </summary>
    public static string FormatArgb(int color) => $"#{color:x8}";

    private static uint HexColor(JsonElement value, string path, int digits)
    {
        var text = value.GetString()!;
        if (!text.StartsWith('#'))
            throw Error(path, $"\"{text}\" isn't a hex colour: it has to start with #");

        if (text.Length - 1 != digits)
            throw Error(path, $"\"{text}\" should have {digits} hex digits");

        var digitsOnly = text.AsSpan(1);
        if (!uint.TryParse(digitsOnly, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var color))
            throw Error(path, $"\"{text}\" isn't a hex colour");

        return color;
    }

    // A list of red, green, blue (and alpha) floats, each made an 8-bit channel as vanilla's ARGB.colorFromFloat does.
    private static int FloatColor(JsonElement value, string path, int channels)
    {
        if (value.GetArrayLength() != channels)
            throw Error(path, $"a colour list needs {channels} floats, not {value.GetArrayLength()}");

        Span<int> parts = stackalloc int[4];
        parts[3] = 255;

        for (var i = 0; i < channels; i++)
            parts[i] = (int)MathF.Floor(Float(value[i], Index(path, i)) * 255f);

        return (parts[3] & 0xFF) << 24 | (parts[0] & 0xFF) << 16 | (parts[1] & 0xFF) << 8 | parts[2] & 0xFF;
    }

    // Like Java's Double.parseDouble, which gives infinity where a number is too large.
    private static double ParseDouble(JsonElement value) =>
        double.Parse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string Format(float number) => number.ToString(CultureInfo.InvariantCulture);

    private static string Format(double number) => number.ToString(CultureInfo.InvariantCulture);
}
