using Obsidian.API.World.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static BlockPredicate HeightPredicate(JsonElement json)
    {
        var low = PredicateAnchor(json.GetProperty("min_inclusive"));
        var high = PredicateAnchor(json.GetProperty("max_inclusive"));

        return (level, position) => position.Y >= low(level) && position.Y <= high(level);
    }

    private static Func<IWorldGenLevel, int> PredicateAnchor(JsonElement json)
    {
        if (json.TryGetProperty("relative_to_sea_level", out var sea))
        {
            var offset = sea.GetInt32();
            return level => level.SeaLevel + offset;
        }

        var anchor = ValueProviders.Anchor(json);
        return level => anchor(level.MinY, level.Height);
    }
}
