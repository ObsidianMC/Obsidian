using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;

namespace Obsidian.SourceGenerators;
public static partial class Extensions
{
    public static string GetJsonFromArray(this ImmutableArray<(string name, string json)> array, string name) =>
        array.FirstOrDefault(x => x.name == name).json;

    public static TypeInformation? GetValue(this Dictionary<string, TypeInformation> source, string key) =>
        source.TryGetValue(key, out var value) ? value : null;
}
