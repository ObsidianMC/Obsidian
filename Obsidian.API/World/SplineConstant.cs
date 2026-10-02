namespace Obsidian.API.World;
public readonly record struct SplineConstant : ISpline
{
    public double Value { get; init; }

    // Vanilla stores spline constants as floats.
    public double MinValue => (float)this.Value;

    public double MaxValue => (float)this.Value;

    public double Apply(double x, double y, double z) => (float)this.Value;
}
