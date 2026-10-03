namespace Obsidian.WorldData.BlockPredicates;

/// <summary>
/// Matches when the block's face in <see cref="Direction"/> can fully support another block.
/// </summary>
[ConfiguredFeatureProperty("minecraft:has_sturdy_face")]
public sealed class HasSturdyFacePredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:has_sturdy_face";

    public Vector Offset { get; init; } = Vector.Zero;

    public required BlockFace Direction { get; init; }

    public bool Test(IWorldGenLevel level, Vector position) => level.GetBlock(position + this.Offset).IsFaceSturdy(this.Direction);
}
