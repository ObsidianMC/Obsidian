using System.Text.Json;
using ApiFeatureSize = Obsidian.API.World.Features.Trees.FeatureSize;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>
/// A tree's <c>minimum_size</c>: the radius around the trunk that must be free at each height, and the height a tree
/// may be clipped to instead of failing.
/// </summary>
internal abstract class FeatureSize(int? minClippedHeight)
{
    public int? MinClippedHeight { get; } = minClippedHeight;

    public abstract int SizeAtHeight(int height, int y);

    public static FeatureSize Read(FeatureRegistry registry, JsonElement json)
    {
        var type = json.GetProperty("type").GetString();
        int? minClippedHeight = json.TryGetProperty("min_clipped_height", out var clipped) ? clipped.GetInt32() : null;

        return type switch
        {
            "minecraft:two_layers_feature_size" => new TwoLayers(
                Field(json, "limit", 1),
                Field(json, "lower_size", 0),
                Field(json, "upper_size", 1),
                minClippedHeight),
            "minecraft:three_layers_feature_size" => new ThreeLayers(
                Field(json, "limit", 1),
                Field(json, "upper_limit", 1),
                Field(json, "lower_size", 0),
                Field(json, "middle_size", 1),
                Field(json, "upper_size", 1),
                minClippedHeight),
            _ => new PluginFeatureSize(registry.PluginValue<ApiFeatureSize>(json, type, "feature size"))
        };
    }

    private static int Field(JsonElement json, string name, int fallback) =>
        json.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;

    private sealed class TwoLayers(int limit, int lowerSize, int upperSize, int? minClippedHeight)
        : FeatureSize(minClippedHeight)
    {
        public override int SizeAtHeight(int height, int y) => y < limit ? lowerSize : upperSize;
    }

    private sealed class ThreeLayers(
        int limit,
        int upperLimit,
        int lowerSize,
        int middleSize,
        int upperSize,
        int? minClippedHeight)
        : FeatureSize(minClippedHeight)
    {
        public override int SizeAtHeight(int height, int y)
        {
            if (y < limit)
            {
                return lowerSize;
            }

            return y >= height - upperLimit ? upperSize : middleSize;
        }
    }
}
