using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    // Every child runs, including children after a rejection or success. Unlike sequence, no child short-circuits.
    private Feature Overlay(JsonElement json)
    {
        var children = json.GetProperty("features").EnumerateArray().Select(this.Placed).ToArray();

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
