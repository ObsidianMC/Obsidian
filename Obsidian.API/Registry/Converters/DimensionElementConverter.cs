using Obsidian.API.Registry.Codecs.Dimensions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.API.Registry.Converters;
internal class DimensionElementConverter : JsonConverter<DimensionElement>
{
    private readonly Dictionary<string, PropertyInfo> propertyMap;

    public DimensionElementConverter()
    {
        this.propertyMap = new();

        foreach (var property in typeof(DimensionElement).GetProperties())
            this.propertyMap.Add(property.Name, property);
    }

    public override DimensionElement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject got: {reader.TokenType}");

        var dimensionElement = new DimensionElement();

        var props = typeToConvert.GetProperties();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return dimensionElement;

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
                    if (reader.TokenType == JsonTokenType.Number)
                        dimensionElement.MonsterSpawnLightLevel = new()
                        {
                            IntValue = reader.GetInt32()
                        };
                    else
                        dimensionElement.MonsterSpawnLightLevel = JsonSerializer.Deserialize<MonsterSpawnLightLevel>(ref reader, options);

                    continue;
                }

                action.Invoke(dimensionElement, ref reader, property);
            }
        }

        throw new JsonException($"Expected EndObject got: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, DimensionElement value, JsonSerializerOptions options)
    {

    }
}

