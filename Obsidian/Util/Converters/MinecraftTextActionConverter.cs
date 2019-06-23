using System;
using Newtonsoft.Json;
using Obsidian.Chat;

namespace Obsidian.Util.Converters
{
    public class MinecraftTextActionConverter : JsonConverter<ETextAction>
    {
        public override ETextAction ReadJson(JsonReader reader, Type objectType, ETextAction existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, ETextAction value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString().ToLower());
        }
    }
}
