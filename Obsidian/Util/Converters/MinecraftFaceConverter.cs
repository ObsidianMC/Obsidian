using Newtonsoft.Json;
using Obsidian.Util.Registry;
using System;

namespace Obsidian.Util.Converters
{
    public class MinecraftFaceConverter : JsonConverter<Face>
    {
        public override Face ReadJson(JsonReader reader, Type objectType, Face existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out Face result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, Face value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
