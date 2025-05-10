namespace Obsidian.API.World;
public readonly struct SplineConstant : ISpline
{
    public double Value { get; init; }

    public double MinValue => Value;

    public double MaxValue => Value;

    public double Apply(double x, double y, double z) => Value;
}

