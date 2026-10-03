namespace Obsidian.WorldData.BlockPredicates;

/// <summary>
/// Matches blocks that can be replaced (air, fluids, short grass...).
/// </summary>
[ConfiguredFeatureProperty("minecraft:replaceable")]
public sealed class ReplaceablePredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:replaceable";

    /// <summary>
    /// Offset from the tested position to the block that is checked.
    /// </summary>
    public Vector Offset { get; init; } = Vector.Zero;

    public bool Test(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position + this.Offset);
        return block.CanBeReplaced();
    }
}
