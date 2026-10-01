namespace Obsidian.WorldData.Features;

/// <summary>
/// Tries each entry in order with its own chance and places the first that wins, otherwise the default feature.
/// </summary>
[ConfiguredFeatureClass("minecraft:random_selector")]
public sealed class RandomSelectorFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:random_selector";

    public required WeightedPlacedFeature[] Features { get; init; }

    public required PlacedFeature Default { get; init; }

    public override bool Place(FeatureContext context)
    {
        if (!context.Level.EnsureCanWrite(context.Origin))
            return false;

        foreach (var entry in this.Features)
        {
            if (context.Random.NextFloat() < entry.Chance)
                return entry.Feature.Place(context.Level, context.Generation, context.Random, context.Origin);
        }

        return this.Default.Place(context.Level, context.Generation, context.Random, context.Origin);
    }
}

/// <summary>
/// A placed feature with the chance it is picked by a <see cref="RandomSelectorFeature"/>.
/// </summary>
public sealed class WeightedPlacedFeature
{
    public required PlacedFeature Feature { get; init; }

    public required float Chance { get; init; }
}
