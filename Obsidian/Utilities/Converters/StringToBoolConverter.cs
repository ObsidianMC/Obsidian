using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters;

public sealed class StringToBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return bool.Parse(reader.GetString());
        else if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
            return reader.GetBoolean();

        throw new InvalidOperationException("Failed to convert string to bool.");
    }
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => throw new NotImplementedException();
}

public sealed class IntToBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return Convert.ToBoolean(reader.GetInt32());
        else if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
            return reader.GetBoolean();

        throw new InvalidOperationException("Failed to convert string to bool.");
    }
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => throw new NotImplementedException();
}
