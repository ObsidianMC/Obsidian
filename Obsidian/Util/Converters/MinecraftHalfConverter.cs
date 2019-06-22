using Newtonsoft.Json;
using Obsidian.Util.Registry;
using System;

namespace Obsidian.Util.Converters
{
    public class MinecraftHalfConverter : JsonConverter<Half>
    {
        public override Half ReadJson(JsonReader reader, Type objectType, Half existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out Half result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, Half value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
