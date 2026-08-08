namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:square")]
public sealed class SquareDensityFunction : IDensityFunction
{
    public string Type => "minecraft:square";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Math.Max(0.0, Argument.MinValue);

    public double MaxValue => Math.Max(Argument.MinValue * Argument.MinValue, Argument.MaxValue * Argument.MaxValue);

    public double GetValue(double x, double y, double z)
    {
        var val = Argument.GetValue(x, y, z);
        return val * val;
    }
}
