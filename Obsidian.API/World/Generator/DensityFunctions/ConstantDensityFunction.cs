namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:constant")]
public sealed class ConstantDensityFunction : IDensityFunction
{
    public string Type => "minecraft:constant";

    public required double Argument { get; init; }

    public double MinValue => Argument;

    public double MaxValue => Argument;

    public double GetValue(double x, double y, double z) => Argument;
}
