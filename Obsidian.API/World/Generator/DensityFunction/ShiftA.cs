using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class ShiftA : IDensityFunction
{
    public string Type => "minecraft:shift_a";

    public required INoise argument;

    public double GetValue(double x, double y, double z) => argument.GetValue(x/4f, 0, z/4f) * 4;
}
