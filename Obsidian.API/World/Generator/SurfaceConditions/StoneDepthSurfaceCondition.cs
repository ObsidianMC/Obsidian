namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Tests distance through stone from a floor or ceiling against a depth threshold.
/// </summary>
[SurfaceCondition("minecraft:stone_depth")]
public sealed record class StoneDepthSurfaceCondition : ISurfaceCondition
{
    /// <summary>
    /// Gets the surface condition codec type identifier.
    /// </summary>
    public string Type => "minecraft:stone_depth";

    /// <summary>
    /// Gets the constant depth adjustment in blocks, read from <c>offset</c>.
    /// </summary>
    public required int Offset { get; init; }

    /// <summary>
    /// Gets whether the sampled surface depth is added to the threshold.
    /// </summary>
    public required bool AddSurfaceDepth { get; init; }

    /// <summary>
    /// Gets the range used to scale secondary surface noise into additional depth.
    /// </summary>
    public required int SecondaryDepthRange { get; init; }

    /// <summary>
    /// Gets the depth reference, <c>floor</c> or <c>ceiling</c>, read from <c>surface_type</c>.
    /// </summary>
    public required string SurfaceType { get; init; }
}
