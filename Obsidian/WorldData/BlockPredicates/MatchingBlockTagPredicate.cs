namespace Obsidian.WorldData.Features.BlocksPredicates;
public sealed class MatchingBlockTagPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:matching_block_tag";

    public List<int> Offset { get; init; } = [0, 0, 0];
    public required string Tag { get; init; }

    public bool GetResult(BlockPredicateContext context) =>
        throw new NotImplementedException();
}
