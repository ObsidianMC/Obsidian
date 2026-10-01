namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:spline")]
public sealed class SplineDensityFunction : IDensityFunction
{
    public string Type => "minecraft:spline";

    public required Spline Spline { get; init; }

    public double MinValue => this.Spline.MinValue;

    public double MaxValue => this.Spline.MaxValue;

    public double GetValue(double x, double y, double z) => this.Spline.Apply(x, y, z);

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new SplineDensityFunction { Spline = this.Spline.MapAll(visitor) });
}
