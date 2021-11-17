using Obsidian.API.Crafting;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters;

public class DefaultEnumConverter<T> : JsonConverter<T>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum && typeToConvert == typeof(T);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();

        if (value.StartsWith("minecraft:"))
            value = value.TrimMinecraftTag();

        return Enum.TryParse(typeof(T), value.Replace("_", ""), true, out var result) ? (T)result : throw new InvalidOperationException($"Failed to deserialize: {value}");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString().ToSnakeCase());
}

public class CraftingTypeConverter : JsonConverter<CraftingType>
{
    public override CraftingType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString().TrimMinecraftTag();

        return Enum.TryParse<CraftingType>(value, true, out var result) ? result : throw new InvalidOperationException($"Failed to deserialize: {value}");
    }

    public override void Write(Utf8JsonWriter writer, CraftingType value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString().ToSnakeCase());
}
