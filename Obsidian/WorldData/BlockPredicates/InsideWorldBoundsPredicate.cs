namespace Obsidian.WorldData.BlockPredicates;

/// <summary>
/// Matches positions inside the build height.
/// </summary>
[ConfiguredFeatureProperty("minecraft:inside_world_bounds")]
public sealed class InsideWorldBoundsPredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:inside_world_bounds";

    public Vector Offset { get; init; } = Vector.Zero;

    public bool Test(IWorldGenLevel level, Vector position) => !level.IsOutsideBuildHeight((position + this.Offset).Y);
}
