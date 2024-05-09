namespace Obsidian.API.World.Generator.DensityFunction;

[DensityFunction("minecraft:abs")]
public sealed class Abs : IDensityFunction
{
    public string Type => "minecraft:abs";

    public required IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z) => Math.Abs(Argument.GetValue(x, y, z));
}
