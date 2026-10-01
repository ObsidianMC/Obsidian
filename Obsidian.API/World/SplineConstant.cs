namespace Obsidian.API.World;
public readonly struct SplineConstant : ISpline
{
    public double Value { get; init; }

    public float MinValue => (float)Value;

    public float MaxValue => (float)Value;

    public float Apply(double x, double y, double z) => (float)Value;
}
