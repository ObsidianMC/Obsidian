using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class Constant : IDensityFunction
{
    public new string Type => "minecraft:constant";

    public required double argument;

    public double GetValue(double x, double y, double z) => argument;
}
