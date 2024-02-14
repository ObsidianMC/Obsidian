using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class Squeeze : IDensityFunction
{
    public string Type => "minecraft:squeeze";

    public required IDensityFunction argument;

    public double GetValue(double x, double y, double z)
    {
        double val = argument.GetValue(x, y, z);
        val = Math.Clamp(val, -1f, 1f);
        return (val / 2D) - (val * val * val / 24D);
    }
}
