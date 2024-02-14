using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class HalfNegative : IDensityFunction
{
    public string Type => "minecraft:half_negative";

    public required IDensityFunction argument;

    public double GetValue(double x, double y, double z)
    {
        var val = argument.GetValue(x, y, z);
        return val < 0 ? val / 2D : val;
    }
}

