using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree;
public class TwoLayersFeatureSize : TreeSizeBase
{
    public override string Type => "two_layers_feature_size";

    [Range(0, 81)]
    public virtual int Limit { get; set; } = 1;

    [Range(0, 16)]
    public virtual int LowerSize { get; set; }

    [Range(0, 16)]
    public virtual int UpperSize { get; set; } = 1;
}
