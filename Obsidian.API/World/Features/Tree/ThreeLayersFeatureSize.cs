using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree;
public class ThreeLayersFeatureSize : TwoLayersFeatureSize
{
    public override string Type => "three_layers_feature_size";

    [Range(0, 80)]
    public override int Limit { get; set; } = 1;

    [Range(0, 80)]
    public int UpperLimit { get; set; } = 1;

    [Range(0, 16)]
    public int MiddleSize { get; set; } = 1;
}
