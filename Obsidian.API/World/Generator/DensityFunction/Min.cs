using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class Min : IDensityFunction
{
    public string Type => "minecraft:min";

    public required IDensityFunction argument1;
    public required IDensityFunction argument2;

    public double GetValue(double x, double y, double z) => Math.Min(argument1.GetValue(x, y, z), argument2.GetValue(x, y, z));
}
