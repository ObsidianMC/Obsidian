namespace Obsidian.WorldData.BlockPredicates;
public sealed class WouldSurvivePredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:would_survive";

    public List<int> Offset { get; init; } = [0, 0, 0];

    /// <summary>
    /// Checks whether this block state can survive in the specified position.
    /// </summary>
    public required IBlockState State { get; init; }

    public bool GetResult(BlockPredicateContext context)
    {

        return false;
    }
}
