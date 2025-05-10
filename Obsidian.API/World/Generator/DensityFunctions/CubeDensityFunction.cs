namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:cube")]
public sealed class CubeDensityFunction : IDensityFunction
{
    public string Type => "minecraft:cube";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => -MaxValue;

    public double MaxValue => Math.Pow(Argument.MaxValue, 3);

    public double GetValue(double x, double y, double z) => Math.Pow(Argument.GetValue(x, y, z), 3);
}
