namespace Obsidian.WorldData.Features;

/// <summary>
/// Vanilla's <c>flower</c> feature type: a <see cref="RandomPatchFeature"/> registered under another id
/// (bone meal uses it to pick flowers).
/// </summary>
[ConfiguredFeatureClass("minecraft:flower")]
public sealed class FlowerFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:flower";

    public int Tries { get; init; } = 128;

    public int XzSpread { get; init; } = 7;

    public int YSpread { get; init; } = 3;

    public required PlacedFeature Feature { get; init; }

    public override bool Place(FeatureContext context) =>
        context.Level.EnsureCanWrite(context.Origin)
        && RandomPatchFeature.PlacePatch(context, this.Tries, this.XzSpread, this.YSpread, this.Feature);
}
