namespace Obsidian.API.World.Generator.DensityFunction;
public sealed class Squeeze : IDensityFunction
{
    public string Type => "minecraft:squeeze";

    public required IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z)
    {
        double val = Argument.GetValue(x, y, z);
        val = Math.Clamp(val, -1f, 1f);
        return (val / 2D) - (val * val * val / 24D);
    }
}
