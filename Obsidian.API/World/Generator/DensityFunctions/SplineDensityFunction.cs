namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:spline")]
public sealed class SplineDensityFunction : IDensityFunction
{
    public string Type => "minecraft:spline";

    /// <summary>
    /// The spline: a multipoint <see cref="World.Spline"/>, or a <see cref="SplineConstant"/>, as vanilla's codec
    /// takes a number for a constant one.
    /// </summary>
    public required ISpline Spline { get; init; }

    public double MinValue => this.Spline.MinValue;

    public double MaxValue => this.Spline.MaxValue;

    public double GetValue(double x, double y, double z) => this.Spline.Apply(x, y, z);

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new SplineDensityFunction { Spline = this.Spline.MapAll(visitor) });
}
