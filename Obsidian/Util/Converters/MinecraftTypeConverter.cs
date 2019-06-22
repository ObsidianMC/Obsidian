using System;
using Newtonsoft.Json;
using Obsidian.Util.Registry;

namespace Obsidian.Util.Converters
{
    public class MinecraftTypeConverter : JsonConverter<MinecraftType>
    {
        public override MinecraftType ReadJson(JsonReader reader, Type objectType, MinecraftType existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out MinecraftType result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, MinecraftType value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
