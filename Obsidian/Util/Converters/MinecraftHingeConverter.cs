using Newtonsoft.Json;
using Obsidian.Util.Registry;
using System;

namespace Obsidian.Util.Converters
{
    public class MinecraftHingeConverter : JsonConverter<Hinge>
    {
        public override Hinge ReadJson(JsonReader reader, Type objectType, Hinge existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out Hinge result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, Hinge value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
