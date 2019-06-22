using Newtonsoft.Json;
using Obsidian.Util.Registry;
using System;

namespace Obsidian.Util.Converters
{
    public class MinecraftFaceConverter : JsonConverter<SignFace>
    {
        public override SignFace ReadJson(JsonReader reader, Type objectType, SignFace existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out SignFace result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, SignFace value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
