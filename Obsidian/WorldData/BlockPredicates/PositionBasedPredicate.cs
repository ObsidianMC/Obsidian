using Obsidian.API.Utilities;

namespace Obsidian.WorldData.Features.BlocksPredicates;

public sealed class PositionBasedPredicate : IBlockPredicate
{
    /// <remarks>
    /// Valid types are inside_world_bounds, solid, replaceable.
    /// </remarks>
    public required string Type { get; init; }

    public List<int> Offset { get; init; } = [0, 0, 0];

    public bool GetResult(BlockPredicateContext context)
    {
        var offset = this.Offset.ToVector();
        
        return false;
    }
}
