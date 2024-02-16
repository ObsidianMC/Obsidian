namespace Obsidian.WorldData.BlockPredicates;
public sealed class NotPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:not";

    /// <summary>
    /// The block predicate to invert.
    /// </summary>
    public required IBlockPredicate Predicate { get; init; }

    public bool GetResult(BlockPredicateContext context) =>
         !this.Predicate.GetResult(context);
}
