namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:quarter_negative")]
public sealed class QuarterNegativeDensityFunction : IDensityFunction
{
    public string Type => "minecraft:quarter_negative";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Transform(Argument.MinValue);

    public double MaxValue => Transform(Argument.MaxValue);

    public double GetValue(double x, double y, double z) => Transform(Argument.GetValue(x, y, z));

    private static double Transform(double value) => value > 0.0 ? value : value * 0.25;
}
