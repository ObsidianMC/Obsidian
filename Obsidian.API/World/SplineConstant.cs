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
    /// <inheritdoc/>
    public double MinValue => (float)this.Value;

    /// <inheritdoc/>
    public double MaxValue => (float)this.Value;

    /// <inheritdoc/>
    public double Apply(double x, double y, double z) => (float)this.Value;
}
