namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:mul")]
public sealed class MulDensityFunction : IDensityFunction
{
    public string Type => "minecraft:mul";

    public required IDensityFunction Argument1 { get; init; }
    public required IDensityFunction Argument2 { get; init; }

    public double MinValue => Argument1.MinValue * Argument2.MinValue;

    public double MaxValue => Argument1.MaxValue * Argument2.MaxValue;

    public double GetValue(double x, double y, double z) => Argument1.GetValue(x, y, z) * Argument2.GetValue(x, y, z);
}
