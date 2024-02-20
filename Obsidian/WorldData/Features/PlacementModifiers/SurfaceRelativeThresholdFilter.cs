namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Returns the current position if the surface is inside a range. 
/// Otherwise returns empty.
/// </summary>
[TreeProperty("minecraft:surface_relative_threshold_filter")]
public sealed class SurfaceRelativeThresholdFilter : PlacementModifierBase
{
    public override string Type { get; internal init; } = "minecraft:surface_relative_threshold_filter";

    /// <summary>
    /// The heightmap to use. One of MOTION_BLOCKING, MOTION_BLOCKING_NO_LEAVES, OCEAN_FLOOR, OCEAN_FLOOR_WG, 
    /// WORLD_SURFACE or WORLD_SURFACE_WG.
    /// </summary>
    public required string Heightmap { get; init; }

    /// <summary>
    /// The minimum relative height from the surface to current position.
    /// </summary>
    public required int MinInclusive { get; init; }

    /// <summary>
    /// The maximum relative height from the surface to current position.
    /// </summary>
    public required int MaxInclusive { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
