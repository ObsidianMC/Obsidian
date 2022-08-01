using System.Reflection;
using System.Text.Json;

namespace Obsidian.API.Registry.Converters;

internal static class ConverterHelpers
{
    public delegate void ActionRef(object obj, ref Utf8JsonReader reader, PropertyInfo propertyInfo);

    private static readonly Dictionary<Type, ActionRef> simpleTypeMap = new();

    static ConverterHelpers()
    {
        simpleTypeMap.Add(typeof(string), (object obj, ref Utf8JsonReader reader, PropertyInfo propertyInfo)
            => propertyInfo.SetValue(obj, reader.GetString()));
        simpleTypeMap.Add(typeof(int), (object obj, ref Utf8JsonReader reader, PropertyInfo propertyInfo)
            => propertyInfo.SetValue(obj, reader.GetInt32()));
        simpleTypeMap.Add(typeof(long?), (object obj, ref Utf8JsonReader reader, PropertyInfo propertyInfo)
            => propertyInfo.SetValue(obj, reader.GetInt64()));
        simpleTypeMap.Add(typeof(long), (object obj, ref Utf8JsonReader reader, PropertyInfo propertyInfo)
            => propertyInfo.SetValue(obj, reader.GetInt64()));
        simpleTypeMap.Add(typeof(float), (object obj, ref Utf8JsonReader reader, PropertyInfo propertyInfo)
            => propertyInfo.SetValue(obj, reader.GetSingle()));
        simpleTypeMap.Add(typeof(bool), (object obj, ref Utf8JsonReader reader, PropertyInfo propertyInfo)
            => propertyInfo.SetValue(obj, reader.TokenType is JsonTokenType.Number ? Convert.ToBoolean(reader.GetInt32()) : reader.GetBoolean()));
    }

    public static bool TryGetAction(Type type, out ActionRef action) => simpleTypeMap.TryGetValue(type, out action);
}

