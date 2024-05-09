namespace Obsidian.API.World.Generator.DensityFunction;

[DensityFunction("minecraft:blend_density")]
public sealed class BlendDensity : IDensityFunction
{
    public string Type => "minecraft:blend_density";

    public required IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z) => Argument.GetValue(x, y, z); // No-op
}
