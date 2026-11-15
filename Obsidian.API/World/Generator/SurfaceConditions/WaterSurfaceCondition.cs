namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Tests a position against the water height with optional stone and surface depth adjustments.
/// </summary>
[SurfaceCondition("minecraft:water")]
public sealed record class WaterSurfaceCondition : ISurfaceCondition
{
    /// <summary>
    /// Gets the surface condition codec type identifier.
    /// </summary>
    public string Type => "minecraft:water";

    /// <summary>
    /// Gets the water-height adjustment in blocks, read from <c>offset</c>.
    /// </summary>
    public required int Offset { get; init; }

    /// <summary>
    /// Gets the multiplier applied to the sampled surface depth in the height comparison.
    /// </summary>
    public required int SurfaceDepthMultiplier { get; init; }

    /// <summary>
    /// Gets whether the stone depth above the position participates in the comparison.
    /// </summary>
    public required bool AddStoneDepth { get; init; }
}
