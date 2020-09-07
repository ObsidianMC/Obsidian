using Newtonsoft.Json;
using Obsidian.Util.Registry;
using System;

namespace Obsidian.Util.Converters
{
    public class MinecraftAttachmentConverter : JsonConverter<Attachment>
    {
        public override Attachment ReadJson(JsonReader reader, Type objectType, Attachment existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out Attachment result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, Attachment value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString().ToLower());
        }
    }
}
