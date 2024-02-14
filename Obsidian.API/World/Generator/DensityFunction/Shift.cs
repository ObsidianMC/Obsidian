using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class Shift : IDensityFunction
{
    public string Type => "minecraft:shift";

    public required INoise argument;

    public double GetValue(double x, double y, double z) => argument.GetValue(x/4D, y/4D, z/4D) * 4;
}
