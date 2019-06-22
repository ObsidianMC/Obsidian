using System;
using Newtonsoft.Json;
using Obsidian.Util.Registry;

namespace Obsidian.Util.Converters
{
    public class MinecraftShapeConverter : JsonConverter<Shape>
    {
        public override Shape ReadJson(JsonReader reader, Type objectType, Shape existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            Enum.TryParse(val, true, out Shape result);

            return result;
        }

        public override void WriteJson(JsonWriter writer, Shape value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
