namespace Obsidian.WorldData.BlockPredicates;

/// <summary>
/// Matches when no entity collides with the block. Always true during world generation, which has no entities yet.
/// </summary>
[ConfiguredFeatureProperty("minecraft:unobstructed")]
public sealed class UnobstructedPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:unobstructed";

    public Vector Offset { get; init; } = Vector.Zero;

    public bool Test(IWorldGenLevel level, Vector position) => true;
}
