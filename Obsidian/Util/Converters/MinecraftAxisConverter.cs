using System;
using Newtonsoft.Json;
using Obsidian.Util.Registry;

namespace Obsidian.Util.Converters
{
    public class MinecraftAxisConverter : JsonConverter<Axis>
    {
        public override Axis ReadJson(JsonReader reader, Type objectType, Axis existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out Axis result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, Axis value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString().ToLower());
        }
    }
}
