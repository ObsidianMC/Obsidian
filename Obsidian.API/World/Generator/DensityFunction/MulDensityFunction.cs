namespace Obsidian.API.World.Generator.DensityFunction;

[DensityFunction("minecraft:mul")]
public sealed class MulDensityFunction : IDensityFunction
{
    public string Type => "minecraft:mul";

    public required IDensityFunction Argument1 { get; init; }
    public required IDensityFunction Argument2 { get; init; }

    public double GetValue(double x, double y, double z) => Argument1.GetValue(x, y, z) * Argument2.GetValue(x, y, z);
}
