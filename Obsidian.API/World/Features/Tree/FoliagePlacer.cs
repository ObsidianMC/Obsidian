namespace Obsidian.API.World.Features.Tree;

public abstract class FoliagePlacer
{
    public abstract string Type { get; init; }
    public virtual IIntProvider Radius { get; set; } = default!;
    public virtual IIntProvider Offset { get; set; } = default!;

    /// <summary>
    /// Gets the foliage height for this placer.
    /// </summary>
    public abstract int GetFoliageHeight(Random random, int treeHeight);

    /// <summary>
    /// Gets the foliage radius from the Radius provider.
    /// </summary>
    public virtual int FoliageRadius(Random random, int trunkHeight)
    {
        return Radius.Get();
    }

    /// <summary>
    /// Gets the vertical offset from the Offset provider.
    /// </summary>
    protected virtual int GetOffset(Random random)
    {
        return Offset.Get();
    }

    /// <summary>
    /// Places the foliage (leaves) for the tree.
    /// </summary>
    /// <param name="context">The feature context containing world and placement information.</param>
    /// <param name="trunkPositions">The list of positions where trunk blocks were placed (foliage attachments).</param>
    /// <param name="treeHeight">The total height of the tree.</param>
    /// <param name="foliageBlock">The block to use for foliage placement.</param>
    /// <returns>A list of positions where foliage blocks were placed (used for decorators).</returns>
    public abstract ValueTask<List<Vector>> Place(FeatureContext context, List<Vector> trunkPositions, int treeHeight, IBlock foliageBlock);

    /// <summary>
    /// Determines if a specific position should be skipped during foliage placement.
    /// </summary>
    /// <param name="random">Random source.</param>
    /// <param name="dx">X distance from center (absolute).</param>
    /// <param name="y">Y offset from layer center.</param>
    /// <param name="dz">Z distance from center (absolute).</param>
    /// <param name="currentRadius">The radius of the current layer.</param>
    /// <param name="doubleTrunk">Whether this is for a 2x2 trunk tree.</param>
    /// <returns>True if this location should be skipped.</returns>
    protected abstract bool ShouldSkipLocation(Random random, int dx, int y, int dz, int currentRadius, bool doubleTrunk);
}
