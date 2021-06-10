using Newtonsoft.Json;
using Obsidian.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace Obsidian.Utilities.Converters
{
    [Obsolete]
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

    // TODO: After changeover complete, remove fully qualified names
    public sealed class StringEnumConverter<TEnum> : System.Text.Json.Serialization.JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private readonly Dictionary<TEnum, string> _enumsToNames;
        private readonly Dictionary<string, TEnum> _namesToEnums;

        private readonly bool _asInteger;

        public StringEnumConverter(JsonNamingPolicy? namingPolicy = null, bool asInteger = false)
        {
            _enumsToNames = new Dictionary<TEnum, string>();
            _namesToEnums = new Dictionary<string, TEnum>();

            _asInteger = asInteger;

            foreach (var value in Enum.GetValues<TEnum>())
            {
                var name = namingPolicy?.ConvertName(value.ToString()) ?? value.ToString();

                _enumsToNames.Add(value, name);
                _namesToEnums.Add(name, value);
            }
        }

        /// <inheritdoc />
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            TEnum result;

            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var value = reader.GetString();
                    if (value is null)
                    {
                        throw new System.Text.Json.JsonException();
                    }

                    if (Enum.TryParse(value, out result))
                    {
                        break;
                    }

                    if (!_namesToEnums.TryGetValue(value, out result))
                    {
                        var caseInsensitiveKey =
                            _namesToEnums.Keys.FirstOrDefault(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));

                        if (caseInsensitiveKey is null)
                        {
                            throw new System.Text.Json.JsonException("Failed to deserialize an enumeration value.");
                        }

                        result = _namesToEnums[caseInsensitiveKey];
                    }

                    break;
                }
                default:
                {
                    throw new System.Text.Json.JsonException("Invalid type for enum deserialization.");
                }
            }

            if (!reader.IsFinalBlock && !reader.Read())
            {
                throw new System.Text.Json.JsonException();
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            if (_asInteger)
            {
                writer.WriteStringValue(Enum.GetUnderlyingType(typeof(TEnum)).IsUnsigned()
                ? Convert.ToUInt64(value).ToString()
                : Convert.ToInt64(value).ToString());

                return;
            }

            writer.WriteStringValue(_enumsToNames[value]);
        }
    }
}
