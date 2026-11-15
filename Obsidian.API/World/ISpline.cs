namespace Obsidian.API.World;

/// <summary>
/// A cubic spline used for terrain shaping.
/// </summary>
/// <remarks>
/// Vanilla evaluates splines in single precision; implementations should return float values widened to double.
/// </remarks>
public interface ISpline
{
    /// <summary>
    /// Gets the lower bound reported for this spline's output.
    /// </summary>
    public double MinValue { get; }
    /// <summary>
    /// Gets the upper bound reported for this spline's output.
    /// </summary>
    public double MaxValue { get; }

    /// <summary>
    /// Evaluates the spline at world block coordinates, returning a single-precision result widened to double.
    /// </summary>
    public double Apply(double x, double y, double z);

    /// <summary>
    /// Maps the density functions used as spline coordinates. See <see cref="IDensityFunction.MapAll"/>.
    /// </summary>
    public ISpline MapAll(IDensityFunctionVisitor visitor) => this;
}
