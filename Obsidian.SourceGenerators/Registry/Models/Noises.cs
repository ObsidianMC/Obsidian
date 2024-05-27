using System.Collections.Immutable;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;
internal sealed class Noises
{
    public BaseFeature[] Settings { get; private set; } = [];

    public static Noises Get(ImmutableArray<(string name, string json)> files)
    {
        return new()
        {
            Settings = ParseSettings(files)
        };
    }

    private static BaseFeature[] ParseSettings(ImmutableArray<(string name, string json)> files)
    {
        var features = new List<BaseFeature>();

        foreach (var (name, json) in files)
        {
            var properties = JsonSerializer.Deserialize<JsonElement>(json)!;

            features.Add(new()
            {
                Name = name,
                Properties = properties.EnumerateObject().ToList()
            });
        }

        return [.. features];
    }
}
