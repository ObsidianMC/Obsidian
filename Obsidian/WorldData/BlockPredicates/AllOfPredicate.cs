namespace Obsidian.WorldData.BlockPredicates;

/// <summary>
/// Matches when every predicate matches (stops at the first failure).
/// </summary>
[ConfiguredFeatureProperty("minecraft:all_of")]
public sealed class AllOfPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:all_of";

    public required IBlockPredicate[] Predicates { get; init; }

    public bool Test(IWorldGenLevel level, Vector position) => this.Predicates.All(predicate => predicate.Test(level, position));
}
