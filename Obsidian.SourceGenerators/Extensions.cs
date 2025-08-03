using Obsidian.SourceGenerators.Registry;
using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;

namespace Obsidian.SourceGenerators;
public static partial class Extensions
{
    public static string GetJsonFromArray(this ImmutableArray<(string name, string json)> array, string name) =>
        array.FirstOrDefault(x => x.name == name).json;

    public static TypeInformation? GetValue(this Dictionary<string, TypeInformation> source, string key) =>
        source.TryGetValue(key, out var value) ? value : null;
    internal static string CompileName(this Tag tag, bool last = false)
    {
        if(last)
            return tag.Type == tag.Parent ? $"{tag.Type.ToPascalCase()}.{tag.PropertyName}" : $"{tag.Parent.ToPascalCase()}.{tag.Type.GetActualType(1).ToPascalCase()}.{tag.PropertyName}";

        return tag.Type == tag.Parent ? tag.PropertyName : $"{tag.Type.GetActualType(1).ToPascalCase()}.{tag.PropertyName}";
    }

    public static void Deconstruct<TKey, TValue>(this IGrouping<TKey, TValue> grouping, out TKey key, out List<TValue> values)
    {
        key = grouping.Key;
        values = grouping.ToList();
    }

    public static string GetActualType(this string type, int index = 0) =>
      type.Contains("/") ? type.Split('/')[index] : type;
}
