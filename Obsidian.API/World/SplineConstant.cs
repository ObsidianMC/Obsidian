namespace Obsidian.API.World;
/// <summary>
/// Represents a position-independent spline value evaluated in single precision.
/// </summary>
public readonly record struct SplineConstant : ISpline
{
    /// <summary>
    /// Gets the constant value, converted to single precision during evaluation.
    /// </summary>
    public double Value { get; init; }

    // Vanilla stores spline constants as floats.
    /// <summary>
    /// Gets the lower bound reported for this spline's output.
    /// </summary>
    public double MinValue => (float)this.Value;

    /// <summary>
    /// Gets the upper bound reported for this spline's output.
    /// </summary>
    public double MaxValue => (float)this.Value;

    /// <summary>
    /// Evaluates the spline at world block coordinates, returning a single-precision result widened to double.
    /// </summary>
    public double Apply(double x, double y, double z) => (float)this.Value;
}
