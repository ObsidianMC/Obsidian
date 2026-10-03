namespace Obsidian.WorldData.Features;

/// <summary>
/// Vanilla's <c>no_bonemeal_flower</c> feature type: a <see cref="RandomPatchFeature"/> that bone meal ignores.
/// </summary>
[ConfiguredFeatureClass("minecraft:no_bonemeal_flower")]
public sealed class NoBonemealFlowerFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:no_bonemeal_flower";

    public int Tries { get; init; } = 128;

    public int XzSpread { get; init; } = 7;

    public int YSpread { get; init; } = 3;

    public required PlacedFeature Feature { get; init; }

    public override bool Place(FeatureContext context) =>
        context.Level.EnsureCanWrite(context.Origin)
        && RandomPatchFeature.PlacePatch(context, this.Tries, this.XzSpread, this.YSpread, this.Feature);
}
