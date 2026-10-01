namespace Obsidian.API.World;

/// <summary>
/// A cubic spline evaluated in single precision, matching vanilla terrain shaping.
/// </summary>
public interface ISpline
{
    public float MinValue { get; }
    public float MaxValue { get; }

    public float Apply(double x, double y, double z);

    /// <summary>
    /// Maps the density functions used as spline coordinates. See <see cref="IDensityFunction.MapAll"/>.
    /// </summary>
    public ISpline MapAll(IDensityFunctionVisitor visitor) => this;
}
