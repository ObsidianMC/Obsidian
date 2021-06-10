using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Obsidian.Utilities.Converters;

namespace Obsidian.Utilities
{
    public static partial class Extensions
    {
        public static JsonSerializerOptions AddConverter<TConverter>(this JsonSerializerOptions options)
            where TConverter : JsonConverter, new()
        {
            options.Converters.Add(new TConverter());

            return options;
        }

        public static JsonSerializerOptions AddStringEnumConverter<TEnum>
        (
            this JsonSerializerOptions options,
            bool asInteger = false
        )
            where TEnum : struct, Enum
        {
            var enumConverter = new StringEnumConverter<TEnum>(options.PropertyNamingPolicy, asInteger);
            options.Converters.Add(enumConverter);
            return options;
        }
    }
}
