using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Keeps the position when its Y is within <c>[MinInclusive, MaxInclusive]</c> of the heightmap.
/// </summary>
[ConfiguredFeatureProperty("minecraft:surface_relative_threshold_filter")]
public sealed class SurfaceRelativeThresholdFilter : PlacementFilterBase
{
    public override string Type => "minecraft:surface_relative_threshold_filter";

    public required HeightmapType Heightmap { get; init; }

    public int MinInclusive { get; init; } = int.MinValue;

    public int MaxInclusive { get; init; } = int.MaxValue;

    // Long math like vanilla, so the open-ended defaults don't overflow.
    protected override bool ShouldPlace(PlacementContext context, IRandomSource random, Vector position)
    {
        long height = context.Level.GetHeight(this.Heightmap, position.X, position.Z);
        return height + this.MinInclusive <= position.Y && position.Y <= height + this.MaxInclusive;
    }
}
