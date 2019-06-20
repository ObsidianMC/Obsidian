using System;
using Newtonsoft.Json;
using Obsidian.Entities;

namespace Obsidian.Util.Converters
{
    public class GeneratorConverter : JsonConverter<Generator>
    {
        public override Generator ReadJson(JsonReader reader, Type objectType, Generator existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = (string)reader.Value;

            val = val.Capitalize();

            Enum.TryParse(val, out Generator result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, Generator value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString().ToLower());
        }
    }
}
