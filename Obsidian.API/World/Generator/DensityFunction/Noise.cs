using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class Noise : IDensityFunction
{
    public string Type => "minecraft:noise";

    public required INoise noise;

    public double xz_scale = 1.0f;

    public double y_scale = 1.0f;

    public double GetValue(double x, double y, double z) => noise.GetValue(x * xz_scale, y * y_scale, z * xz_scale);
}
