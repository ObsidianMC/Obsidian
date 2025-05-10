namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:max")]
public sealed class MaxDensityFunction : IDensityFunction
{
    public string Type => "minecraft:max";

    public required IDensityFunction Argument1 { get; init; }
    public required IDensityFunction Argument2 { get; init; }

    public double MinValue => Math.Max(Argument1.MinValue, Argument2.MinValue);

    public double MaxValue => Math.Max(Argument1.MaxValue, Argument2.MaxValue);

    public double GetValue(double x, double y, double z) => Math.Max(Argument1.GetValue(x, y, z), Argument2.GetValue(x, y, z));
}
