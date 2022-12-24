using System.Collections.Immutable;

namespace Obsidian.SourceGenerators;
public static class Extensions
{
    public static string GetJsonFromArray(this ImmutableArray<(string name, string json)> array, string name) =>
        array.FirstOrDefault(x => x.name == name).json;
}
