using Obsidian.Nbt;
using System.Text.Json;

namespace Obsidian.API.Utilities;

/// <summary>
/// Converts JSON to NBT, so that a data pack file's components (a recipe result's, a loot table's set_components)
/// can be read like a saved stack's: vanilla's component codecs read either.
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
        // Booleans stay booleans, which readers take as bytes too and which are saved as bytes, like vanilla's; a
        // translation's argument shows one as true or false.
        JsonValueKind.True => new NbtTag<bool>(name, true),
        JsonValueKind.False => new NbtTag<bool>(name, false),
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
            return MixedList(name, elements);

        var list = new NbtList(type, name);
        foreach (var element in elements)
            list.Add(element);

        return list;
    }

    // A list whose elements differ in type, like [1, 1.5] or ["a", {"text": "b"}], saved like vanilla saves a mixed
    // list: as compounds, each element that isn't one (or is one that looks wrapped) under an empty key, so that each
    // keeps its type. Readers unwrap them (see Extensions.ListElements).
    private static NbtList MixedList(string name, List<INbtTag> elements)
    {
        var list = new NbtList(NbtTagType.Compound, name);
        foreach (var element in elements)
            list.Add(IsUnwrapped(element) ? element : Wrap(element));

        return list;
    }

    // Compounds only take named tags when built, so the element goes in as a read tag, like a palette entry.
    private static NbtCompound Wrap(INbtTag element)
    {
        var wrapper = new NbtCompound();
        element.Name = string.Empty;
        wrapper.SetReadTag(string.Empty, element);

        return wrapper;
    }

    private static bool IsUnwrapped(INbtTag element) =>
        element is NbtCompound compound && !(compound.Count == 1 && compound.HasTag(string.Empty));

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
