using Obsidian.Nbt;
using System.Text.Json;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// Converts NBT written as JSON in data files (vanilla's <c>CompoundTag.CODEC</c> through JSON) back to NBT.
/// </summary>
internal static class JsonNbt
{
    /// <summary>
    /// The compound for a JSON object. Integral numbers become ints (longs when too large) and other numbers doubles,
    /// booleans bytes, arrays lists.
    /// </summary>
    public static NbtCompound ToCompound(string json)
    {
        using var document = JsonDocument.Parse(json);
        return (NbtCompound)Convert(document.RootElement, string.Empty);
    }

    private static INbtTag Convert(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var compound = new NbtCompound(name);
                foreach (var property in element.EnumerateObject())
                    compound.Add(property.Name, Convert(property.Value, property.Name));

                return compound;
            case JsonValueKind.Array:
                var items = element.EnumerateArray().Select(item => Convert(item, string.Empty)).ToList();
                var list = new NbtList(items.Count == 0 ? NbtTagType.End : items[0].Type, name);
                foreach (var item in items)
                {
                    item.Name = null;
                    list.Add(item);
                }

                return list;
            case JsonValueKind.String:
                return new NbtTag<string>(name, element.GetString());
            case JsonValueKind.True or JsonValueKind.False:
                return new NbtTag<byte>(name, element.GetBoolean() ? (byte)1 : (byte)0);
            default:
                if (element.TryGetInt32(out var intValue))
                    return new NbtTag<int>(name, intValue);

                return element.TryGetInt64(out var longValue) ? new NbtTag<long>(name, longValue) : new NbtTag<double>(name, element.GetDouble());
        }
    }
}
