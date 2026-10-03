using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.BlockPredicates;

/// <summary>
/// Matches when the block is one of <see cref="Blocks"/>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:matching_blocks")]
public sealed class MatchingBlocksPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:matching_blocks";

    /// <summary>
    /// Offset from the tested position to the block that is checked.
    /// </summary>
    public Vector Offset { get; init; } = Vector.Zero;

    public required BlockSet Blocks { get; init; }

    public bool Test(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position + this.Offset);
        return this.Blocks.Contains(block);
    }
}
