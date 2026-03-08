using System.Collections.Immutable;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;
internal sealed class Features
{
    public BaseFeature[] TreeFeatures { get; private set; } = [];
    public BaseFeature[] FlowerFeatures { get; private set; } = [];

    public static Features Get(ImmutableArray<(string name, string json)> files)
    {
        var treesJson = files.GetJsonFromArray("trees");
        var flowersJson = files.GetJsonFromArray("flowers");
        return new()
        {
            TreeFeatures = ParseFeatures(treesJson),
            FlowerFeatures = ParseFeatures(flowersJson)
        };
    }

    private static BaseFeature[] ParseFeatures(string json)
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
