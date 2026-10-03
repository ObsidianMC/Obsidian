using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.BlockPredicates;

/// <summary>
/// Matches when the block is in <see cref="Tag"/> (e.g. <c>minecraft:logs</c>).
/// </summary>
[ConfiguredFeatureProperty("minecraft:matching_block_tag")]
public sealed class MatchingBlockTagPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:matching_block_tag";

    /// <summary>
    /// Offset from the tested position to the block that is checked.
    /// </summary>
    public Vector Offset { get; init; } = Vector.Zero;

    public required string Tag { get; init; }

    private BlockSet TagSet => field ??= new BlockSet($"#{this.Tag}");

    public bool Test(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position + this.Offset);
        return this.TagSet.Contains(block);
    }
}
