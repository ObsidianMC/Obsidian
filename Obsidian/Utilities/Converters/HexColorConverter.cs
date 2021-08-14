using Obsidian.API;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters
{
    public class HexColorConverter : JsonConverter<HexColor>
    {
        public override HexColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new HexColor(reader.GetString());

        public override void Write(Utf8JsonWriter writer, HexColor value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString() ?? string.Empty);
    }
}
