using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree;

[TreeProperty("minecraft:two_layers_feature_size")]
public class TwoLayersFeatureSize : TreeSizeBase
{
    public override string Type { get; init; } = "minecraft:two_layers_feature_size";

    [Range(0, 81)]
    public virtual int Limit { get; set; } = 1;

    [Range(0, 16)]
    public virtual int LowerSize { get; set; }

    [Range(0, 16)]
    public virtual int UpperSize { get; set; } = 1;
}
