using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters
{
    public class DefaultObjectConverter : JsonConverter<object>
    {
        public override bool CanConvert(Type typeToConvert) => true;
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value is null)
                return null;
            if (value.EndsWith("F"))
                return float.Parse(value.TrimEnd('F'));
            else if (value.EndsWith("B"))
                return value.TrimEnd('B') == "1";
            else if (value.EndsWith('L'))
                return long.Parse(value.TrimEnd('L'));

            throw new InvalidOperationException("Failed to read json.");
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) => throw new NotImplementedException();
    }
}
