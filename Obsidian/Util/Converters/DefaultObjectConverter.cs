using Newtonsoft.Json;
using System;

namespace Obsidian.Util.Converters
{
    public class DefaultObjectConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(string);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var value = reader.Value;

            if (value is null)
                return null;

            var val = value.ToString();

            if (val.EndsWith("F"))
            {
                val = val.TrimEnd('F');

                return float.Parse(val);
            }
            else if (val.EndsWith("B"))
            {
                val = val.TrimEnd('B');
                return val == "1";
            }else if (val.EndsWith('L'))
            {
                val = val.TrimEnd('L');
                return long.Parse(val);
            }

            return reader.Value ?? null;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            
        }
    }
}
