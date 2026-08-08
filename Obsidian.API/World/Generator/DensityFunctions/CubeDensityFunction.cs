namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:cube")]
public sealed class CubeDensityFunction : IDensityFunction
{
    public string Type => "minecraft:cube";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Argument.MinValue * Argument.MinValue * Argument.MinValue;

    public double MaxValue => Argument.MaxValue * Argument.MaxValue * Argument.MaxValue;

    public double GetValue(double x, double y, double z)
    {
        var val = Argument.GetValue(x, y, z);
        return val * val * val;
    }
}
