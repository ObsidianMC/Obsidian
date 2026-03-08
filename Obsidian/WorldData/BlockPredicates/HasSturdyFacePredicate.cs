namespace Obsidian.WorldData.Features.BlocksPredicates;

[ConfiguredFeatureProperty("minecraft:has_sturdy_face")]
public sealed class HasSturdyFacePredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:has_sturdy_face";

    public List<int> Offset { get; init; } = [0, 0, 0];
    public required string Direction { get; init; }

    public bool GetResult(BlockPredicateContext context)
    {
        return false;
    }
}
