using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class Clamp : IDensityFunction
{
    public string Type => "minecraft:clamp";

    public required IDensityFunction input;

    public required double min;
    public required double max;

    public double GetValue(double x, double y, double z) => Math.Clamp(input.GetValue(x, y, z), min, max);
}
