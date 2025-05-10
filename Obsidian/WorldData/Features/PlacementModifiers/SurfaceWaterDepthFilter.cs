namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// If the number of blocks of a motion blocking material under the surface (the top non-air block) is less than the specified depth,
/// return the current position.
/// Otherwise return
/// </summary>
[TreeProperty("minecraft:surface_water_depth_filter")]
public sealed class SurfaceWaterDepthFilter : PlacementModifierBase
{
    public override string Type { get; internal init; } = "minecraft:surface_water_depth_filter";

    /// <summary>
    /// The maximum allowed depth.
    /// </summary>
    public required int MaxWaterDepth { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
