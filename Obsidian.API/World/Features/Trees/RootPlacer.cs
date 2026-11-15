using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features.Trees;

/// <summary>
/// Places roots below a raised trunk, like vanilla's <c>RootPlacer</c> (only mangroves use one). Plugins add types
/// with <c>RegisterWorldgenType&lt;RootPlacer, T&gt;</c>.
/// </summary>
public abstract class RootPlacer
{
    /// <summary>
    /// Gets the registered codec type identifier for this world generation component.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>How far above the feature origin the trunk starts.</summary>
    public required IIntProvider TrunkOffsetY { get; init; }

    /// <summary>
    /// Gets the block state provider used for root positions.
    /// </summary>
    public required IBlockStateProvider RootProvider { get; init; }

    /// <summary>
    /// Gets the optional block placement above roots, or <c>null</c> when disabled.
    /// </summary>
    public AboveRootPlacement? AboveRootPlacement { get; init; }

    /// <summary>
    /// Places the roots between <paramref name="origin"/> and <paramref name="trunkOrigin"/>; <c>false</c> aborts the
    /// tree.
    /// </summary>
    public abstract bool PlaceRoots(TreeContext tree, Vector origin, Vector trunkOrigin);

    /// <summary>
    /// Offsets the world block origin upward by a sampled <see cref="TrunkOffsetY"/>.
    /// </summary>
    public Vector GetTrunkOrigin(Vector origin, IRandomSource random) =>
        origin + (0, this.TrunkOffsetY.Sample(random), 0);

    /// <summary>Vanilla <c>TreeFeature.validTreePos</c>: air or <c>#replaceable_by_trees</c>.</summary>
    protected virtual bool CanPlaceRoot(IWorldGenLevel level, Vector position) =>
        TreeBlockRules.ValidTreePos(level, position);

    /// <summary>
    /// Places a root at an eligible world position, waterlogging it and optionally placing a block above it.
    /// </summary>
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
    /// <summary>
    /// Gets the state provider for blocks placed immediately above roots.
    /// </summary>
    public required IBlockStateProvider AboveRootProvider { get; init; }

    /// <summary>
    /// Gets the probability of placing a block above an eligible root, read from <c>above_root_placement_chance</c>.
    /// </summary>
    public required float AboveRootPlacementChance { get; init; }
}
