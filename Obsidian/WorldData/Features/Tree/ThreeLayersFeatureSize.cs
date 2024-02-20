using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree;

[TreeProperty("minecraft:three_layers_feature_size")]
public class ThreeLayersFeatureSize : TwoLayersFeatureSize
{
    public override string Type { get; init; } = "minecraft:three_layers_feature_size";

    [Range(0, 80)]
    public override int Limit { get; set; } = 1;

    [Range(0, 80)]
    public int UpperLimit { get; set; } = 1;

    [Range(0, 16)]
    public int MiddleSize { get; set; } = 1;
}
