using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features.Trees;

/// <summary>
/// Places roots below a raised trunk, like vanilla's <c>RootPlacer</c> (only mangroves use one). Plugins add types
/// with <c>RegisterWorldgenType&lt;RootPlacer, T&gt;</c>.
/// </summary>
public abstract class RootPlacer
{
    public string Type { get; init; } = string.Empty;

    /// <summary>How far above the feature origin the trunk starts.</summary>
    public required IIntProvider TrunkOffsetY { get; init; }

    public required IBlockStateProvider RootProvider { get; init; }

    public AboveRootPlacement? AboveRootPlacement { get; init; }

    /// <summary>
    /// Places the roots between <paramref name="origin"/> and <paramref name="trunkOrigin"/>; <c>false</c> aborts the
    /// tree.
    /// </summary>
    public abstract bool PlaceRoots(TreeContext tree, Vector origin, Vector trunkOrigin);

    public Vector GetTrunkOrigin(Vector origin, IRandomSource random) =>
        origin + (0, this.TrunkOffsetY.Sample(random), 0);

    /// <summary>Vanilla <c>TreeFeature.validTreePos</c>: air or <c>#replaceable_by_trees</c>.</summary>
    protected virtual bool CanPlaceRoot(IWorldGenLevel level, Vector position) =>
        TreeBlockRules.ValidTreePos(level, position);

    protected virtual void PlaceRoot(TreeContext tree, Vector position)
    {
        if (!this.CanPlaceRoot(tree.Level, position))
            return;

        var root = this.RootProvider.GetState(tree.Random, position);
        tree.SetRoot(position, GetPotentiallyWaterloggedState(tree.Level, position, root));

        var above = this.AboveRootPlacement;
        if (above is null)
            return;

        var abovePosition = position + Vector.Up;
        if (tree.Random.NextFloat() < above.AboveRootPlacementChance && tree.Level.GetBlock(abovePosition).IsAir)
        {
            var block = above.AboveRootProvider.GetState(tree.Random, abovePosition);
            tree.SetRoot(abovePosition, GetPotentiallyWaterloggedState(tree.Level, abovePosition, block));
        }
    }

    /// <summary>
    /// Waterlogs states that support it when the position holds any water (vanilla <c>FluidTags.WATER</c>).
    /// </summary>
    protected static IBlock GetPotentiallyWaterloggedState(IWorldGenLevel level, Vector position, IBlock block)
    {
        if (!TreeBlockRules.HasProperty(block, "waterlogged"))
            return block;

        var water = TreeBlockRules.States.IsWater(level.GetBlock(position));
        return TreeBlockRules.WithProperty(block, "waterlogged", water);
    }
}

/// <summary>
/// Blocks occasionally placed on top of roots (moss carpet for mangroves), like vanilla's <c>AboveRootPlacement</c>.
/// </summary>
public sealed class AboveRootPlacement
{
    public required IBlockStateProvider AboveRootProvider { get; init; }

    public required float AboveRootPlacementChance { get; init; }
}
