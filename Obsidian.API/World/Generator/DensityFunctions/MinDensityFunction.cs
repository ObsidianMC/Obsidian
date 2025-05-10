namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:min")]
public sealed class MinDensityFunction : IDensityFunction
{
    public string Type => "minecraft:min";

    public required IDensityFunction Argument1 { get; init; }
    public required IDensityFunction Argument2 { get; init; }

    public double MinValue => Math.Min(Argument1.MinValue, Argument2.MinValue);

    public double MaxValue => Math.Min(Argument1.MaxValue, Argument2.MaxValue);

    public double GetValue(double x, double y, double z) => Math.Min(Argument1.GetValue(x, y, z), Argument2.GetValue(x, y, z));
}
