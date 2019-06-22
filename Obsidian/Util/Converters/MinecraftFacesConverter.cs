using Newtonsoft.Json;
using Obsidian.Util.Registry;
using System;

namespace Obsidian.Util.Converters
{
    public class MinecraftFacesConverter : JsonConverter<BlockFace>
    {
        public override BlockFace ReadJson(JsonReader reader, Type objectType, BlockFace existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out BlockFace result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, BlockFace value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString().ToLower());
        }
    }
}
