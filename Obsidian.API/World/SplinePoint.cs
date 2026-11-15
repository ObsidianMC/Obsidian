namespace Obsidian.API.World;
/// <summary>
/// Defines a terrain spline control point with a value and endpoint slope.
/// </summary>
public readonly record struct SplinePoint
{
    /// <summary>
    /// Gets the slope used for interpolation and extrapolation at this point.
    /// </summary>
    public required double Derivative { get; init; }
    /// <summary>
    /// Gets this point's position in the spline's density-function coordinate space.
    /// </summary>
    public required double Location { get; init; }

    /// <summary>
    /// Gets the constant or nested spline evaluated for this point.
    /// </summary>
    public required ISpline Value { get; init; }
}
