using Newtonsoft.Json;
using Obsidian.Util.Registry;
using System;

namespace Obsidian.Util.Converters
{
    public class MinecraftCustomDirectionConverter : JsonConverter<CustomDirection>
    {
        public override CustomDirection ReadJson(JsonReader reader, Type objectType, CustomDirection existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out CustomDirection result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, CustomDirection value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
