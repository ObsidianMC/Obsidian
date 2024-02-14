using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class Cube : IDensityFunction
{
    public string Type => "minecraft:cube";

    public required IDensityFunction argument;

    public double GetValue(double x, double y, double z) => Math.Pow(argument.GetValue(x, y, z), 3);
}
