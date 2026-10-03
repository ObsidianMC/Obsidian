namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Free radius <see cref="LowerSize"/> below <see cref="Limit"/>, <see cref="UpperSize"/> in the top
/// <see cref="UpperLimit"/> blocks and <see cref="MiddleSize"/> in between.
/// </summary>
[ConfiguredFeatureProperty("minecraft:three_layers_feature_size")]
public sealed class ThreeLayersFeatureSize : FeatureSize
{
    public int Limit { get; init; } = 1;

    public int UpperLimit { get; init; } = 1;

    public int LowerSize { get; init; }

    public int MiddleSize { get; init; } = 1;

    public int UpperSize { get; init; } = 1;

    public override int GetSizeAtHeight(int height, int y)
    {
        if (y < this.Limit)
            return this.LowerSize;

        return y >= height - this.UpperLimit ? this.UpperSize : this.MiddleSize;
    }
}
