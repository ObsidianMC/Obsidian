namespace Obsidian.WorldData.BlockPredicates;
public sealed class MatchingBlocksPredicate : IBlockPredicate
{
    public string Type { get; } = "minecraft:matching_blocks";

    public List<int> Offset { get; init; } = [0, 0, 0];
    /// <summary>
    /// The blocks that will match. 
    /// Can be a block ID or a block tag, or a list of block IDs.
    /// </summary>
    public required List<string> Blocks { get; init; } = [];

    public bool GetResult(BlockPredicateContext context)
    {

        return false;
    }
}
