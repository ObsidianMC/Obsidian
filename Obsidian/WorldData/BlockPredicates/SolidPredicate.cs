namespace Obsidian.WorldData.BlockPredicates;

/// <summary>
/// Matches solid blocks (vanilla's deprecated <c>isSolid</c>).
/// </summary>
[ConfiguredFeatureProperty("minecraft:solid")]
public sealed class SolidPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:solid";

    /// <summary>
    /// Offset from the tested position to the block that is checked.
    /// </summary>
    public Vector Offset { get; init; } = Vector.Zero;

    public bool Test(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position + this.Offset);
        return block.IsSolid();
    }
}
