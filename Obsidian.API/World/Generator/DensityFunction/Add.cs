namespace Obsidian.API.World.Generator.DensityFunction;
public sealed class Add : IDensityFunction
{
    public string Type => "minecraft:add";

    public required IDensityFunction Argument1 { get; init; }

    public required IDensityFunction Argument2 { get; init; }

    public double GetValue(double x, double y, double z) => Argument1.GetValue(x, y, z) + Argument2.GetValue(x, y, z);
}
