using Obsidian.API;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Obsidian.Tests.Utilities;

/// <summary>
/// Puts Obsidian's saved data and packets in the forms vanilla probes write them: NBT as the JSON vanilla's
/// <c>NbtOps</c> converts it to (bytes and booleans as numbers), compared by value; numbers as Java prints them; and
/// sounds as "id source x,y,z volume pitch" relative to an origin.
/// </summary>
internal static class NbtJson
{
    public static JsonNode? ToJson(INbtTag tag) => tag switch
    {
        NbtCompound compound => new JsonObject(compound.Select(pair =>
            new System.Collections.Generic.KeyValuePair<string, JsonNode?>(pair.Key, ToJson(pair.Value)))),
        NbtList list => new JsonArray(list.Select(ToJson).ToArray()),
        NbtTag<bool> flag => JsonValue.Create(flag.Value ? 1 : 0),
        NbtTag<byte> value => JsonValue.Create(value.Value),
        NbtTag<sbyte> value => JsonValue.Create(value.Value),
        NbtTag<short> value => JsonValue.Create(value.Value),
        NbtTag<int> value => JsonValue.Create(value.Value),
        NbtTag<long> value => JsonValue.Create(value.Value),
        NbtTag<float> value => JsonValue.Create(value.Value),
        NbtTag<double> value => JsonValue.Create(value.Value),
        NbtTag<string> value => JsonValue.Create(value.Value),
        NbtArray<int> array => new JsonArray(array.GetArray().Select(value => (JsonNode?)value).ToArray()),
        NbtArray<long> array => new JsonArray(array.GetArray().Select(value => (JsonNode?)value).ToArray()),
        NbtArray<byte> array => new JsonArray(array.GetArray().Select(value => (JsonNode?)value).ToArray()),
        _ => throw new ArgumentException($"Unexpected tag {tag.GetType()}")
    };

    /// <summary>Whether two JSON values are equal, with numbers compared by value and keys in any order.</summary>
    public static bool Same(JsonNode? expected, JsonNode? actual) => Canonical(expected) == Canonical(actual);

    /// <summary>The value with sorted keys and numbers as Java prints doubles, for comparing and for messages.</summary>
    public static string Canonical(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject value => "{" + string.Join(",", value
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + ":" + Canonical(pair.Value))) + "}",
        JsonArray value => "[" + string.Join(",", value.Select(Canonical)) + "]",
        JsonValue value when value.TryGetValue(out string? text) => "\"" + text + "\"",
        JsonValue value when value.TryGetValue(out bool flag) => flag ? "1" : "0",
        JsonValue value => Java(double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture)),
        _ => node.ToJsonString()
    };

    /// <summary>A number as Java's <c>Double.toString</c> prints it, for the values probes write.</summary>
    public static string Java(double value)
    {
        if (value == Math.Floor(value) && Math.Abs(value) < 1e7)
            return value.ToString("0.0", CultureInfo.InvariantCulture);

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>A sound packet as the probes write a bystander's sounds.</summary>
    public static string Sound(SoundPacket sound, Vector origin)
    {
        var x = sound.SoundPosition.X / 8.0 - origin.X;
        var y = sound.SoundPosition.Y / 8.0 - origin.Y;
        var z = sound.SoundPosition.Z / 8.0 - origin.Z;
        return $"{sound.SoundLocation} {Category(sound.Category)} {Java(x)},{Java(y)},{Java(z)} "
            + $"{Java(sound.Volume)} {Java(sound.Pitch)}";
    }

    // Vanilla's SoundSource names.
    private static string Category(SoundCategory category) => category switch
    {
        SoundCategory.Records => "record",
        SoundCategory.Blocks => "block",
        SoundCategory.Players => "player",
        _ => category.ToString().ToLowerInvariant()
    };
}
