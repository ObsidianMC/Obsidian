using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    // Every child runs, including children after a rejection or success. Unlike sequence, no child short-circuits.
    private Feature Overlay(JsonElement json)
    {
        var features = json.GetProperty("features");
        var children = features.ValueKind == JsonValueKind.Array
            ? features.EnumerateArray().Select(this.Placed).ToArray()
            : [this.Placed(features)];

        return (context, random, origin) =>
        {
            var placed = false;

            foreach (var child in children)
            {
                placed |= child.Place(context, random, origin);
            }

            return placed;
        };
    }
}
