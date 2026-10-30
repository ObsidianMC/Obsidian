using Obsidian.Nbt;
using System.Text.Json;

namespace Obsidian.Utilities.Converters;

/// <summary>
/// Converts JSON to NBT, so that a data pack file's components can be read like a saved stack's: vanilla's component
/// codecs read either.
/// </summary>
/// <remarks>
/// Vanilla converts a whole number to the smallest NBT number that holds it and reads any NBT number back. Obsidian's
/// saved components are read as the types vanilla saves them with, so whole numbers become ints (longs past an int)
/// and lists of them int arrays, as vanilla saves colors and ids.
/// </remarks>
internal static class JsonNbt
{
    public static INbtTag ToNbt(string name, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => ObjectToNbt(name, value),
        JsonValueKind.Array => ArrayToNbt(name, value),
        JsonValueKind.String => new NbtTag<string>(name, value.GetString()),
        JsonValueKind.True => new NbtTag<byte>(name, 1),
        JsonValueKind.False => new NbtTag<byte>(name, 0),
        JsonValueKind.Number => NumberToNbt(name, value),
        _ => throw new JsonException($"NBT can't hold a JSON {value.ValueKind}.")
    };

    private static NbtCompound ObjectToNbt(string name, JsonElement value)
    {
        var compound = new NbtCompound(name);

        foreach (var property in value.EnumerateObject())
            compound.Add(ToNbt(property.Name, property.Value));

        return compound;
    }

    private static INbtTag ArrayToNbt(string name, JsonElement value)
    {
        var elements = value.EnumerateArray()
            .Select(element => ToNbt(string.Empty, element))
            .ToList();

        if (elements.Count > 0 && elements.All(element => element is NbtTag<int>))
        {
            var values = elements.Cast<NbtTag<int>>().Select(element => element.Value).ToArray();
            return new NbtArray<int>(name, values);
        }

        var type = elements.Count > 0 ? elements[0].Type : NbtTagType.End;
        if (elements.Any(element => element.Type != type))
            throw new JsonException($"The elements of an NBT list must be of one type, unlike those of {name}.");

        var list = new NbtList(type, name);
        foreach (var element in elements)
            list.Add(element);

        return list;
    }

    private static INbtTag NumberToNbt(string name, JsonElement value)
    {
        if (value.TryGetInt32(out var integer))
            return new NbtTag<int>(name, integer);

        if (value.TryGetInt64(out var whole))
            return new NbtTag<long>(name, whole);

        // Like vanilla, a number a float holds exactly is a float.
        var number = value.GetDouble();
        var single = (float)number;

        return single == number ? new NbtTag<float>(name, single) : new NbtTag<double>(name, number);
    }
}
