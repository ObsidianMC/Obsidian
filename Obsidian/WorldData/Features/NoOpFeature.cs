namespace Obsidian.WorldData.Features;

/// <summary>
/// Places nothing, like vanilla's NoOpFeature, which data packs use to switch a feature off.
/// </summary>
[ConfiguredFeatureClass("minecraft:no_op")]
public sealed class NoOpFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:no_op";

    // Vanilla's reports success.
    public override bool Place(FeatureContext context) => true;
}
