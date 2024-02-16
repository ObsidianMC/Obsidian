namespace Obsidian.API.World.Features.PlacementModifiers;

/// <summary>
/// Returns the current position when the predicate is passed, otherwise return empty.
/// </summary>
public sealed class BlockPredicateFilterPlacement : PlacementModifierBase
{
    public override string Type => "minecraft:block_predicate_filter";

    /// <summary>
    /// The block predicate to test.
    /// </summary>
    public required IBlockPredicate Predicate { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
