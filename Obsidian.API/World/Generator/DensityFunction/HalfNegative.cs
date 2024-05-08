namespace Obsidian.API.World.Generator.DensityFunction;
public sealed class HalfNegative : IDensityFunction
{
    public string Type => "minecraft:half_negative";

    public required IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z)
    {
        var val = Argument.GetValue(x, y, z);
        return val < 0 ? val / 2D : val;
    }
}

