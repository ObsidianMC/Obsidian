namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Tests a position against a vertical anchor with optional depth adjustments.
/// </summary>
[SurfaceCondition("minecraft:y_above")]
public sealed record class YAboveSurfaceCondition : ISurfaceCondition
{
    /// <summary>
    /// Gets the surface condition codec type identifier.
    /// </summary>
    public string Type => "minecraft:y_above";

    /// <summary>
    /// Gets the reference height, read from <c>anchor</c>.
    /// </summary>
    public required VerticalAnchor Anchor { get; init; }

    /// <summary>
    /// Gets the multiplier applied to the sampled surface depth in the height comparison.
    /// </summary>
    public required int SurfaceDepthMultiplier { get; init; }

    /// <summary>
    /// Gets whether the stone depth above the position participates in the comparison.
    /// </summary>
    public required bool AddStoneDepth { get; init; }
}
