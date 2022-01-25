using Obsidian.API.Registry.Converters;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.API.Registry.Codecs.Dimensions;

internal class DimensionCodecConverter : JsonConverter<DimensionCodec>
{
    private readonly Dictionary<string, PropertyInfo> propertyMap;

    public DimensionCodecConverter()
    {
        this.propertyMap = new();

        foreach (var property in typeof(DimensionCodec).GetProperties())
            this.propertyMap.Add(property.Name, property);
    }

    public override DimensionCodec? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject got: {reader.TokenType}");

        var dimensionCodec = new DimensionCodec();

        var props = typeToConvert.GetProperties();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return dimensionCodec;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected property name got: {reader.TokenType}");

            var propName = reader.GetString();

            reader.Read();

            if (string.IsNullOrWhiteSpace(propName))
                throw new JsonException("Property name was null.");

            foreach (var (name, property) in this.propertyMap)
            {
                var convertedName = options.PropertyNamingPolicy?.ConvertName(name) ?? name;

                if (!propName.Equals(convertedName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!ConverterHelpers.TryGetAction(property.PropertyType, out var action))
                {
                    dimensionCodec.Element = JsonSerializer.Deserialize<DimensionElement>(ref reader, options);
                    continue;
                }

                action.Invoke(dimensionCodec, ref reader, property);
            }
        }

        throw new JsonException($"Expected EndObject got: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, DimensionCodec value, JsonSerializerOptions options)
    {

    }
}

