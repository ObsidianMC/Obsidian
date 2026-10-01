namespace Obsidian.WorldData.BlockPredicates;

[ConfiguredFeatureProperty("minecraft:not")]
public sealed class NotPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:not";

    public required IBlockPredicate Predicate { get; init; }

    public bool Test(IWorldGenLevel level, Vector position) => !this.Predicate.Test(level, position);
}
