namespace Obsidian.API.World.Generator.DensityFunction;

[DensityFunction("minecraft:cube")]
public sealed class Cube : IDensityFunction
{
    public string Type => "minecraft:cube";

    public required IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z) => Math.Pow(Argument.GetValue(x, y, z), 3);
}
