namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Free radius <see cref="LowerSize"/> below <see cref="Limit"/> blocks and <see cref="UpperSize"/> above.
/// </summary>
[ConfiguredFeatureProperty("minecraft:two_layers_feature_size")]
public sealed class TwoLayersFeatureSize : FeatureSize
{
    public int Limit { get; init; } = 1;

    public int LowerSize { get; init; }

    public int UpperSize { get; init; } = 1;

    public override int GetSizeAtHeight(int height, int y) => y < this.Limit ? this.LowerSize : this.UpperSize;
}
