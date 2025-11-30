using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree;

public abstract class TrunkPlacer
{
    public const int MaxHeight = 80;

    public abstract string Type { get; init; }

    [Range(0, 32)]
    public virtual int BaseHeight { get; set; }

    [Range(0, 24)]
    public virtual int HeightRandA { get; set; }

    [Range(0, 24)]
    public virtual int HeightRandB { get; init; }

    /// <summary>
    /// Places the trunk blocks for the tree.
    /// </summary>
    /// <param name="context">The feature context containing world and placement information.</param>
    /// <param name="origin">The base position where the tree trunk starts.</param>
    /// <param name="treeHeight">The total height of the tree.</param>
    /// <param name="trunkBlock">The block to use for trunk placement.</param>
    /// <returns>A list of positions where trunk blocks were placed (used for foliage placement).</returns>
    public abstract ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, int treeHeight, IBlock trunkBlock);

    /// <summary>
    /// Calculates the tree height using base height and random variations.
    /// </summary>
    public virtual int GetTreeHeight(Random random)
    {
        return BaseHeight + random.Next(HeightRandA + 1) + random.Next(HeightRandB + 1);
    }
}
