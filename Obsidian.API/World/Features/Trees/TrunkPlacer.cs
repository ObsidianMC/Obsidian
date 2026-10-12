using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features.Trees;

/// <summary>
/// Places a tree's trunk and reports where foliage attaches, like vanilla's <c>TrunkPlacer</c>. Plugins add types
/// with <c>RegisterWorldgenType&lt;TrunkPlacer, T&gt;</c>.
/// </summary>
public abstract class TrunkPlacer
{
    public string Type { get; init; } = string.Empty;

    public required int BaseHeight { get; init; }

    public required int HeightRandA { get; init; }

    public required int HeightRandB { get; init; }

    /// <summary>Vanilla <c>getTreeHeight</c>: <c>base_height + rand(0..a) + rand(0..b)</c>.</summary>
    public int GetTreeHeight(IRandomSource random) =>
        this.BaseHeight + random.NextInt(this.HeightRandA + 1) + random.NextInt(this.HeightRandB + 1);

    /// <summary>
    /// Places the trunk starting at <paramref name="origin"/> and returns the foliage attachment points.
    /// </summary>
    /// <param name="freeTreeHeight">Height actually available, possibly clipped below the sampled tree height.</param>
    public abstract List<FoliageAttachment> PlaceTrunk(TreeContext tree, int freeTreeHeight, Vector origin);

    /// <summary>Whether a trunk may grow through the position: <see cref="ValidTreePos"/> or an existing log.</summary>
    public bool IsFree(IWorldGenLevel level, Vector position) =>
        this.ValidTreePos(level, position) || TreeBlockRules.Logs.Contains(level.GetBlock(position));

    /// <summary>
    /// Vanilla <c>setDirtAt</c>: replaces the block with the dirt provider unless it's already dirt (forced by
    /// <c>force_dirt</c>).
    /// </summary>
    protected static void SetDirtAt(TreeContext tree, Vector position)
    {
        if (tree.ForceDirt || !TreeBlockRules.IsDirtUnderTrunk(tree.Level.GetBlock(position)))
            tree.SetLog(position, tree.DirtProvider.GetState(tree.Random, position));
    }

    /// <summary>
    /// Places a trunk block if <see cref="ValidTreePos"/>, optionally transforming the provided state (e.g. log axis).
    /// </summary>
    protected bool PlaceLog(TreeContext tree, Vector position, Func<IBlock, IBlock>? transform = null)
    {
        if (!this.ValidTreePos(tree.Level, position))
            return false;

        var block = tree.TrunkProvider.GetState(tree.Random, position);
        tree.SetLog(position, transform is null ? block : transform(block));
        return true;
    }

    protected void PlaceLogIfFree(TreeContext tree, Vector position)
    {
        if (this.IsFree(tree.Level, position))
            this.PlaceLog(tree, position);
    }

    /// <summary>Vanilla <c>TreeFeature.validTreePos</c>: air or <c>#replaceable_by_trees</c>.</summary>
    protected virtual bool ValidTreePos(IWorldGenLevel level, Vector position) =>
        TreeBlockRules.ValidTreePos(level, position);

    /// <summary>Vanilla <c>trySetValue(RotatedPillarBlock.AXIS, axis)</c>.</summary>
    protected static Func<IBlock, IBlock> WithAxis(string axis) =>
        block => TreeBlockRules.WithProperty(block, "axis", axis);

    protected static string AxisOf(BlockFace face) => face switch
    {
        BlockFace.Up or BlockFace.Down => "y",
        BlockFace.North or BlockFace.South => "z",
        _ => "x"
    };
}

/// <summary>
/// Where a foliage placer should place leaves, like vanilla's <c>FoliagePlacer.FoliageAttachment</c>.
/// </summary>
/// <param name="Position">Top of the trunk (or branch) the foliage grows around.</param>
/// <param name="RadiusOffset">Added to the sampled foliage radius.</param>
/// <param name="DoubleTrunk">Whether the trunk is 2x2, which widens rows by one block on the positive axes.</param>
public readonly record struct FoliageAttachment(Vector Position, int RadiusOffset, bool DoubleTrunk);
