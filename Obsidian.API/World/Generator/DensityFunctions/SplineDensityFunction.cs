namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:spline")]
public sealed class SplineDensityFunction : IDensityFunction
{
    public string Type => "minecraft:spline";

    public required Spline Spline { get; init; }

    public double MinValue => Spline.MinValue;

    public double MaxValue => Spline.MaxValue;

    public double GetValue(double x, double y, double z)
    {
        Spline.Create();
        return Spline.Apply(x, y, z);
    }
}
