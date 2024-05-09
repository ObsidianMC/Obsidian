namespace Obsidian.API.World.Generator.DensityFunction;

[DensityFunction("minecraft:half_negative")]
public sealed class HalfNegativeDensityFunction : IDensityFunction
{
    public string Type => "minecraft:half_negative";

    public required IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z)
    {
        var val = Argument.GetValue(x, y, z);
        return val < 0 ? val / 2D : val;
    }
}

