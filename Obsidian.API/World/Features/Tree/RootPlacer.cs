using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree;

public abstract class RootPlacer
{
    public abstract string Type { get; init; }

    public virtual required IBlockStateProvider RootProvider { get; set; }
    public virtual required IIntProvider TrunkOffsetY { get; set; }

    public virtual RootPlacement? AboveRootPlacement { get; set; }

    public Vector GetTrunkOrigin(Vector pos) =>
        pos.Relative(Vector.Up, this.TrunkOffsetY.Get());

    /// <summary>
    /// Places the root blocks for the tree below the trunk origin.
    /// </summary>
    /// <param name="context">The feature context containing world and placement information.</param>
    /// <param name="origin">The base position where roots should be placed.</param>
    /// <param name="trunkOrigin">The position where the trunk will start (after offset).</param>
    /// <returns>A list of positions where root blocks were placed (used for decorators).</returns>
    public abstract ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, Vector trunkOrigin);

    public sealed class RootPlacement
    {
        public required IBlockStateProvider AboveRootProvider { get; init; }

        [Range(0.0, 1.0)]
        public required float AboveRootPlacementChance { get; init; }
    }
}
