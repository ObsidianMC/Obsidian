namespace Obsidian.API.World.Generator.DensityFunction;
public sealed class BlendDensity : IDensityFunction
{
    public string Type => "minecraft:blend_density";

    public required IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z) => Argument.GetValue(x, y, z); // No-op
}
