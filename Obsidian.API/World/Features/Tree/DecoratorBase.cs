using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree;

public abstract class DecoratorBase
{
    public abstract string Type { get; init; }

    [Range(0.0, 1.0)]
    public virtual float Probability { get; set; }

    /// <summary>
    /// Applies decorations to the tree (e.g., beehives, vines, cocoa pods).
    /// </summary>
    /// <param name="context">The feature context containing world and placement information.</param>
    /// <param name="trunkPositions">The list of positions where trunk blocks were placed.</param>
    /// <param name="foliagePositions">The list of positions where foliage blocks were placed.</param>
    /// <param name="rootPositions">The list of positions where root blocks were placed.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract ValueTask Place(FeatureContext context, List<Vector> trunkPositions, List<Vector> foliagePositions, List<Vector> rootPositions);
}
