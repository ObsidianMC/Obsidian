namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Transitions from true to false between two heights using positional randomness.
/// </summary>
[SurfaceCondition("minecraft:vertical_gradient")]
public sealed record class VerticalGradient : ISurfaceCondition
{
    /// <summary>
    /// Gets the surface condition codec type identifier.
    /// </summary>
    public string Type => "minecraft:vertical_gradient";

    /// <summary>
    /// Gets the identifier used to seed the positional random sequence, read from <c>random_name</c>.
    /// </summary>
    public required string RandomName { get; init; }

    /// <summary>
    /// Gets the height at and below which this condition always succeeds.
    /// </summary>
    public required VerticalAnchor TrueAtAndBelow { get; init; }

    /// <summary>
    /// Gets the height at and above which this condition always fails.
    /// </summary>
    public required VerticalAnchor FalseAtAndAbove { get; init; }
}
