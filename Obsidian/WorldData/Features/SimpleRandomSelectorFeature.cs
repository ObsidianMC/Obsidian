namespace Obsidian.WorldData.Features;

/// <summary>
/// Places one of the features, chosen uniformly.
/// </summary>
[ConfiguredFeatureClass("minecraft:simple_random_selector")]
public sealed class SimpleRandomSelectorFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:simple_random_selector";

    public required ImmutableArray<PlacedFeature> Features { get; init; }

    public override bool Place(FeatureContext context) =>
        context.Level.EnsureCanWrite(context.Origin)
        && this.Features[context.Random.NextInt(this.Features.Length)].Place(context.Level, context.Generation, context.Random, context.Origin);
}
