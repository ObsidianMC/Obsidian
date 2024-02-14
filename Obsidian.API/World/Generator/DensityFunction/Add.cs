using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class Add : IDensityFunction
{
    public string Type => "minecraft:add";

    public required IDensityFunction argument1;

    public required IDensityFunction argument2;

    public double GetValue(double x, double y, double z) => argument1.GetValue(x, y, z) + argument2.GetValue(x, y, z);
}
