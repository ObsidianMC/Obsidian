namespace Obsidian.WorldData.Features;

/// <summary>
/// Places one of two features with equal chance.
/// </summary>
[ConfiguredFeatureClass("minecraft:random_boolean_selector")]
public sealed class RandomBooleanSelectorFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:random_boolean_selector";

    public required PlacedFeature FeatureTrue { get; init; }

    public required PlacedFeature FeatureFalse { get; init; }

    public override bool Place(FeatureContext context) =>
        context.Level.EnsureCanWrite(context.Origin)
        && (context.Random.NextBoolean() ? this.FeatureTrue : this.FeatureFalse)
            .Place(context.Level, context.Generation, context.Random, context.Origin);
}
