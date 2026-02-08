namespace Obsidian.WorldData.BlockPredicates;

[ConfiguredFeatureProperty("minecraft:matching_blocks")]
public sealed class MatchingBlocksPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:matching_blocks";

    public List<int> Offset { get; init; } = [0, 0, 0];
    /// <summary>
    /// The blocks that will match. 
    /// Can be a block ID or a block tag, or a list of block IDs.
    /// </summary>
    public string Blocks { get; init; }

    public bool GetResult(BlockPredicateContext context)
    {

        return false;
    }
}
