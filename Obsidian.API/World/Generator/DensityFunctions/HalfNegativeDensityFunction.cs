namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:half_negative")]
public sealed class HalfNegativeDensityFunction : IDensityFunction
{
    public string Type => "minecraft:half_negative";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Argument.MinValue < 0 ? Argument.MinValue/2D : Argument.MinValue;

    public double MaxValue => Argument.MaxValue;

    public double GetValue(double x, double y, double z)
    {
        var val = Argument.GetValue(x, y, z);
        return val < 0 ? val / 2D : val;
    }
}

