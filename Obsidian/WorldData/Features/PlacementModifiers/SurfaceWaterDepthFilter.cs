using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Keeps the position when the water above the ocean floor is at most <see cref="MaxWaterDepth"/> deep.
/// </summary>
[ConfiguredFeatureProperty("minecraft:surface_water_depth_filter")]
public sealed class SurfaceWaterDepthFilter : PlacementFilterBase
{
    public override string Type => "minecraft:surface_water_depth_filter";

    public required int MaxWaterDepth { get; init; }

    protected override bool ShouldPlace(PlacementContext context, IRandomSource random, Vector position)
    {
        var oceanFloor = context.Level.GetHeight(HeightmapType.OceanFloor, position.X, position.Z);
        var surface = context.Level.GetHeight(HeightmapType.WorldSurface, position.X, position.Z);
        return surface - oceanFloor <= this.MaxWaterDepth;
    }
}
