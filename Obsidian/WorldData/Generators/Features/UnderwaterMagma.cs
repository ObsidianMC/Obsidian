using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static Feature UnderwaterMagma(JsonElement json) => SharedAlgorithm(new UnderwaterMagmaFeature
    {
        FloorSearchRange = json.GetProperty("floor_search_range").GetInt32(),
        PlacementRadiusAroundFloor = json.GetProperty("placement_radius_around_floor").GetInt32(),
        PlacementProbabilityPerValidPosition = json.GetProperty("placement_probability_per_valid_position").GetSingle()
    });
}
