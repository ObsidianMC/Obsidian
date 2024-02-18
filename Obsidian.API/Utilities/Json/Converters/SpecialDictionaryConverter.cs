using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.API.Utilities.Json.Converters;
internal sealed class SpecialDictionaryConverter : JsonConverter<Dictionary<string, string[]>>
{
    public override Dictionary<string, string[]> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var doc = JsonDocument.ParseValue(ref reader);

        var dict = new Dictionary<string, string[]>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var name = prop.Name;

            var list = new List<string>();

            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                list.Add(prop.Value.GetString());
            }
            else
            {
                foreach (var val in prop.Value.EnumerateArray())
                    list.Add(val.GetString());
            }

            dict.Add(name, [.. list]);
        }

        return dict;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string[]> value, JsonSerializerOptions options)
    {
        foreach (var (key, values) in value)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(key);

            if (values.Length == 1)
                writer.WriteStringValue(values[0]);
            else
            {
                writer.WriteStartArray();

                foreach (var val in values)
                    writer.WriteStringValue(val);

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
    }
}
