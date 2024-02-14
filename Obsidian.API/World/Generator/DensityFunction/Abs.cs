using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class Abs : IDensityFunction
{
    public string Type => "minecraft:abs";

    public required IDensityFunction argument;

    public double GetValue(double x, double y, double z) => Math.Abs(argument.GetValue(x, y, z));
}
