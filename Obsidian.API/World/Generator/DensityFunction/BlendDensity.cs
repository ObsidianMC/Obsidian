using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class BlendDensity : IDensityFunction
{
    public string Type => "minecraft:blend_density";

    public required IDensityFunction argument;

    public double GetValue(double x, double y, double z) => argument.GetValue(x, y, z); // No-op
}
