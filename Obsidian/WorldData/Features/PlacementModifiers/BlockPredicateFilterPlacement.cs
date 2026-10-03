using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Keeps the position when the block predicate matches.
/// </summary>
[ConfiguredFeatureProperty("minecraft:block_predicate_filter")]
public sealed class BlockPredicateFilterPlacement : PlacementFilterBase
{
    public override string Type => "minecraft:block_predicate_filter";

    public required IBlockPredicate Predicate { get; init; }

    protected override bool ShouldPlace(PlacementContext context, IRandomSource random, Vector position) =>
        this.Predicate.Test(context.Level, position);
}
