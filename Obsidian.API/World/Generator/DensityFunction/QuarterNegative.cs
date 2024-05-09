namespace Obsidian.API.World.Generator.DensityFunction;

[DensityFunction("minecraft:quarter_negative")]
public sealed class QuarterNegative : IDensityFunction
{
    public string Type => "minecraft:quarter_negative";

    public required IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z)
    {
        var val = Argument.GetValue(x, y, z);
        return val < 0 ? val / 4D : val;
    }
}
