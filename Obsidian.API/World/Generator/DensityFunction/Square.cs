using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class Square : IDensityFunction
{
    public string Type => "minecraft:square";

    public required IDensityFunction argument;

    public double GetValue(double x, double y, double z) => (float)Math.Pow(argument.GetValue(x, y, z), 2);
}
