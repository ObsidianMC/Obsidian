namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:squeeze")]
public sealed class SqueezeDensityFunction : IDensityFunction
{
    public string Type => "minecraft:squeeze";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => -0.45832;

    public double MaxValue => 0.45834;

    public double GetValue(double x, double y, double z)
    {
        double val = Argument.GetValue(x, y, z);
        val = Math.Clamp(val, -1f, 1f);
        return (val / 2D) - (val * val * val / 24D);
    }
}
