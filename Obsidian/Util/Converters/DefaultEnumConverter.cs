using Newtonsoft.Json;
using Obsidian.Util.Extensions;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Util.Converters
{
    public class DefaultEnumConverter<T> : JsonConverter<T>
    {
        public override T ReadJson(JsonReader reader, Type objectType, [AllowNull] T existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var val = reader.Value.ToString().Replace("_", "");

            if (Enum.TryParse(typeof(T), val, true, out var result))
                return (T)result;

            throw new InvalidOperationException($"Failed to deserialize: {val}");
        }

        public override void WriteJson(JsonWriter writer, [AllowNull] T value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString().ToSnakeCase());
        }
    }
}
