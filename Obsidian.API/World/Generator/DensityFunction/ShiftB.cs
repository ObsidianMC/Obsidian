using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class ShiftB : IDensityFunction
{
    public string Type => "minecraft:shift_b";

    public required INoise argument;

    public double GetValue(double x, double y, double z) => argument.GetValue(z/4D, x/4D, 0) * 4;
}
