namespace Obsidian.API.World.Generator.DensityFunctions;
[DensityFunction("minecraft:spline")]
public sealed class SplineDensityFunction : IDensityFunction
{
    public string Type => "minecraft:spline";

    public required Spline Spline { get; init; }

    public double GetValue(double x, double y, double z) => throw new NotImplementedException();
}

public readonly struct Spline
{
    public required IDensityFunction Coordinate { get; init; }

    public required object[] Points { get; init; }
}
