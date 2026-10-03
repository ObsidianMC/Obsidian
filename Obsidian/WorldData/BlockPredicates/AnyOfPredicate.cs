namespace Obsidian.WorldData.BlockPredicates;

/// <summary>
/// Matches when any predicate matches (stops at the first match).
/// </summary>
[ConfiguredFeatureProperty("minecraft:any_of")]
public sealed class AnyOfPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:any_of";

    public required ImmutableArray<IBlockPredicate> Predicates { get; init; }

    public bool Test(IWorldGenLevel level, Vector position) => this.Predicates.Any(predicate => predicate.Test(level, position));
}
