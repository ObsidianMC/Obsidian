using System.Collections.Immutable;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;
internal sealed class Features
{
    public BaseFeature[] TreeFeatures { get; private set; } = [];

    public static Features Get(ImmutableArray<(string name, string json)> files)
    {
        var treesJson = files.GetJsonFromArray("trees");
        return new()
        {
            TreeFeatures = ParseTreeFeatures(treesJson)
        };
    }

    private static BaseFeature[] ParseTreeFeatures(string json)
    {
         var dictionary = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        var features = new List<BaseFeature>();

        foreach(var kv in dictionary)
        {
            var identifier = kv.Key;
            var config = kv.Value;

            features.Add(new()
            {
                Name = identifier,
                Properties = config.EnumerateObject().ToList()
            });
        }

        return [.. features];
    }
}
