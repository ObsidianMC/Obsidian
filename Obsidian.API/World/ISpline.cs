namespace Obsidian.API.World;

/// <summary>
/// A cubic spline used for terrain shaping.
/// </summary>
/// <remarks>
/// Vanilla evaluates splines in single precision; implementations should return float values widened to double.
/// </remarks>
public interface ISpline
{
    public double MinValue { get; }
    public double MaxValue { get; }

    public double Apply(double x, double y, double z);

    /// <summary>
    /// Maps the density functions used as spline coordinates. See <see cref="IDensityFunction.MapAll"/>.
    /// </summary>
    public ISpline MapAll(IDensityFunctionVisitor visitor) => this;
}
