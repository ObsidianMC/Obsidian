namespace Obsidian.API.World.Generator.DensityFunction;
public sealed class Square : IDensityFunction
{
    public string Type => "minecraft:square";

    public required IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z) => (float)Math.Pow(Argument.GetValue(x, y, z), 2);
}
