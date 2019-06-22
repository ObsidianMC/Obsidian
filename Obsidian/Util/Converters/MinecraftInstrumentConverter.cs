using Newtonsoft.Json;
using Obsidian.Util.Registry;
using System;

namespace Obsidian.Util.Converters
{
    public class MinecraftInstrumentConverter : JsonConverter<Instruments>
    {
        public override Instruments ReadJson(JsonReader reader, Type objectType, Instruments existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out Instruments result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, Instruments value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString().ToLower());
        }
    }
}
