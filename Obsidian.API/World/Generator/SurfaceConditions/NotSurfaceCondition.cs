namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Negates another surface condition.
/// </summary>
[SurfaceCondition("minecraft:not")]
public sealed record class NotSurfaceCondition : ISurfaceCondition
{
    /// <summary>
    /// Gets the surface condition codec type identifier.
    /// </summary>
    public string Type => "minecraft:not";

    /// <summary>
    /// Gets the condition whose result is negated.
    /// </summary>
    public required ISurfaceCondition Invert { get; init; }
}
