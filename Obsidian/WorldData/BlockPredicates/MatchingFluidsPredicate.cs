namespace Obsidian.WorldData.BlockPredicates;
public sealed class MatchingFluidsPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:matching_fluids";

    public List<int> Offset { get; init; } = [0, 0, 0];
    /// <summary>
    /// The blocks that will match. 
    /// Can be a Fluid ID or a fluid tag, or a list of fluid IDs.
    /// </summary>
    public required List<string> Fluids { get; init; } = [];

    public bool GetResult(BlockPredicateContext context)
    {

        return false;
    }
}
