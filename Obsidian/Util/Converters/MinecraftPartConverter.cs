using System;
using Newtonsoft.Json;
using Obsidian.Util.Registry;

namespace Obsidian.Util.Converters
{
    public class MinecraftPartConverter : JsonConverter<Part>
    {
        public override Part ReadJson(JsonReader reader, Type objectType, Part existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out Part result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, Part value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString().ToLower());
        }
    }
}
